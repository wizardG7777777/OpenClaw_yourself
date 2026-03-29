import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { UnityBridgeClient } from '../client/UnityBridgeClient.js';

const client = new UnityBridgeClient();

beforeAll(async () => {
  await client.connect(process.env.MCP_URL ?? process.env.MCP_WS_URL ?? 'http://127.0.0.1:8080/mcp');
});

afterAll(async () => {
  await client.disconnect();
});

describe('error cases — full error code coverage', () => {
  it('unknown tool → INVALID_TOOL', async () => {
    const r = await client.sendMCPRequest(
      JSON.stringify({ tool: 'summon_dragon', args: {} })
    );
    expect(r.ok).toBe(false);
    expect(r.error?.code).toBe('INVALID_TOOL');
  });

  it('malformed JSON → INVALID_PARAMS with "Malformed JSON"', async () => {
    const r = await client.sendMCPRequest('{ "tool": "get_inventory" ,,, }');
    expect(r.ok).toBe(false);
    expect(r.error?.code).toBe('INVALID_PARAMS');
    expect(r.error?.message).toMatch(/Malformed JSON/i);
  });

  it('missing tool field → INVALID_PARAMS, field="tool"', async () => {
    const r = await client.sendMCPRequest(JSON.stringify({ args: {} }));
    expect(r.ok).toBe(false);
    expect(r.error?.code).toBe('INVALID_PARAMS');
    expect(r.error?.details?.['field']).toBe('tool');
  });

  it('args is an array → INVALID_PARAMS, field="args"', async () => {
    const r = await client.sendMCPRequest(
      JSON.stringify({ tool: 'get_inventory', args: ['bad'] })
    );
    expect(r.ok).toBe(false);
    expect(r.error?.code).toBe('INVALID_PARAMS');
    expect(r.error?.details?.['field']).toBe('args');
  });

  it('move_to without target_id → INVALID_PARAMS mentioning "target_id"', async () => {
    const r = await client.sendMCPRequest(
      JSON.stringify({ tool: 'move_to', args: {} })
    );
    expect(r.ok).toBe(false);
    expect(r.error?.code).toBe('INVALID_PARAMS');
    expect(r.error?.message).toMatch(/target_id/);
  });

  it('equip_item without item_id → INVALID_PARAMS mentioning "item_id"', async () => {
    const r = await client.sendMCPRequest(
      JSON.stringify({ tool: 'equip_item', args: {} })
    );
    expect(r.ok).toBe(false);
    expect(r.error?.code).toBe('INVALID_PARAMS');
    expect(r.error?.message).toMatch(/item_id/);
  });

  it('use_tool_on with both required params missing → INVALID_PARAMS mentioning first required param', async () => {
    const r = await client.sendMCPRequest(
      JSON.stringify({ tool: 'use_tool_on', args: {} })
    );
    expect(r.ok).toBe(false);
    expect(r.error?.code).toBe('INVALID_PARAMS');
    // ParameterNormalizer checks required params in order: tool_id first
    expect(r.error?.message).toMatch(/tool_id/);
  });

  it('move_to with non-existent entity → TARGET_NOT_FOUND', async () => {
    const r = await client.sendMCPRequest(
      JSON.stringify({ tool: 'move_to', args: { target_id: 'entity_that_does_not_exist_xyz' } })
    );
    expect(r.ok).toBe(false);
    expect(r.error?.code).toBe('TARGET_NOT_FOUND');
  });
});
