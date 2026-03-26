import { UnityBridgeClient } from './client/UnityBridgeClient.js';

/**
 * Pre-flight: verify Unity MCP Bridge is reachable before any tests run.
 * Fails with a clear message if Unity is not open or the Bridge is not started.
 */
export async function setup(): Promise<void> {
  const url = process.env.MCP_URL ?? process.env.MCP_WS_URL ?? 'http://127.0.0.1:8080/mcp';
  const client = new UnityBridgeClient();

  try {
    await client.connect(url);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(
      `Cannot connect to Unity MCP server at ${url}: ${message}\n` +
      `Open Unity, then go to Window > MCP For Unity and start the HTTP bridge.`
    );
  } finally {
    await client.disconnect();
  }

  console.log(`[globalSetup] Unity MCP server reachable at ${url}`);
}
