import { randomUUID } from 'crypto';

/** Shape of an MCPResponse as serialized by MCPGameBridgeTool */
export interface MCPGameResponse {
  ok: boolean;
  action_id?: string;
  status?: string;
  cancelled_action_id?: string;
  data?: unknown;
  error?: {
    code: string;
    message: string;
    retryable: boolean;
    details?: Record<string, unknown>;
    suggested_next_actions?: unknown[];
  };
}

type JsonRpcSuccess<T> = {
  jsonrpc: '2.0';
  id: string;
  result: T;
};

type JsonRpcFailure = {
  jsonrpc: '2.0';
  id: string;
  error: {
    code: number;
    message: string;
  };
};

type ExecuteCustomToolResult = {
  content?: Array<{ type: string; text?: string }>;
  structuredContent?: {
    success: boolean;
    message?: string | null;
    error?: string | null;
    data?: MCPGameResponse | null;
    hint?: string | null;
  };
  isError?: boolean;
};

/**
 * HTTP MCP client for the Unity MCP server.
 *
 * Transport:
 *   POST /mcp with JSON-RPC payloads
 *   Response body is event-stream containing a single "message" event
 *
 * Custom tool execution:
 *   tools/call name="execute_custom_tool"
 *   arguments={ tool_name: "mcp_game_bridge", parameters: { raw_json } }
 */
export class UnityBridgeClient {
  private endpoint?: string;
  private sessionId?: string;

  async connect(url = process.env.MCP_URL ?? process.env.MCP_WS_URL ?? 'http://127.0.0.1:8080/mcp'): Promise<void> {
    this.endpoint = url;
    const init = await this.rpc<{
      protocolVersion: string;
      serverInfo: { name: string; version: string };
    }>('initialize', {
      protocolVersion: '2025-03-26',
      capabilities: {},
      clientInfo: { name: 'e2e-tests', version: '1.0.0' },
    }, false);

    if (!init.result?.serverInfo?.name) {
      throw new Error('Unity MCP initialize returned an invalid payload');
    }
  }

  /**
   * Send a raw MCP JSON string and return the parsed MCPGameResponse.
   * MCP-level errors (ok=false) are returned normally.
   * Transport/custom-tool errors are thrown as exceptions.
   */
  async sendMCPRequest(rawJson: string, timeoutMs = 10_000): Promise<MCPGameResponse> {
    if (!this.endpoint || !this.sessionId) {
      throw new Error('MCP client is not connected');
    }

    const response = await this.rpc<ExecuteCustomToolResult>('tools/call', {
      name: 'execute_custom_tool',
      arguments: {
        tool_name: 'mcp_game_bridge',
        parameters: { raw_json: rawJson },
      },
    }, true, timeoutMs);

    if (response.result.isError) {
      const text = response.result.content?.map((entry) => entry.text ?? '').join('\n').trim();
      throw new Error(text || 'execute_custom_tool returned an error');
    }

    const structured = response.result.structuredContent;
    if (!structured?.success) {
      throw new Error(structured?.error || structured?.message || 'Custom tool call failed');
    }

    if (!structured.data) {
      throw new Error('Custom tool call did not return MCP response data');
    }

    return structured.data;
  }

  async disconnect(): Promise<void> {
    this.sessionId = undefined;
    this.endpoint = undefined;
  }

  private async rpc<T>(
    method: string,
    params: Record<string, unknown>,
    includeSession = true,
    timeoutMs = 10_000,
  ): Promise<JsonRpcSuccess<T>> {
    if (!this.endpoint) {
      throw new Error('MCP endpoint is not configured');
    }

    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      'Accept': 'application/json, text/event-stream',
    };

    if (includeSession && this.sessionId) {
      headers['mcp-session-id'] = this.sessionId;
    }

    const response = await fetch(this.endpoint, {
      method: 'POST',
      headers,
      body: JSON.stringify({
        jsonrpc: '2.0',
        id: randomUUID(),
        method,
        params,
      }),
      signal: AbortSignal.timeout(timeoutMs),
    });

    const sessionId = response.headers.get('mcp-session-id');
    if (!this.sessionId && sessionId) {
      this.sessionId = sessionId;
    }

    const payload = this.parseRpcPayload(await response.text()) as JsonRpcSuccess<T> | JsonRpcFailure;
    if ('error' in payload) {
      throw new Error(`MCP ${method} failed: ${payload.error.message}`);
    }
    return payload;
  }

  private parseRpcPayload(raw: string): unknown {
    const trimmed = raw.trim();
    if (!trimmed) {
      throw new Error('Unity MCP returned an empty response');
    }

    if (trimmed.startsWith('{')) {
      return JSON.parse(trimmed);
    }

    const events = trimmed
      .split(/\r?\n\r?\n/)
      .map((chunk) => chunk.trim())
      .filter(Boolean);

    for (const event of events) {
      const dataLines = event
        .split(/\r?\n/)
        .filter((line) => line.startsWith('data:'))
        .map((line) => line.slice(5).trim());

      if (dataLines.length > 0) {
        return JSON.parse(dataLines.join('\n'));
      }
    }

    throw new Error(`Could not parse Unity MCP response: ${trimmed}`);
  }
}
