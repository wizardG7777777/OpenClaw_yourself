import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { UnityBridgeClient, type MCPGameResponse } from '../client/UnityBridgeClient.js';

/**
 * MVP scenario: simulate the Agent's Observe → Decide → Act loop
 * as described in the MCP Agent Design doc §5.
 *
 * Runs sequentially so that each step can use context from prior steps.
 */
const client = new UnityBridgeClient();

beforeAll(async () => {
  await client.connect(process.env.MCP_URL ?? process.env.MCP_WS_URL ?? 'http://127.0.0.1:8080/mcp');
});

afterAll(async () => {
  await client.disconnect();
});

function req(tool: string, args: Record<string, unknown> = {}): string {
  return JSON.stringify({ tool, args });
}

describe('MVP Agent scenario — Observe → Decide → Act', () => {
  let worldSummary: Record<string, unknown>;

  it('Step 1: get_world_summary — observe initial world state', async () => {
    const r = await client.sendMCPRequest(req('get_world_summary'));
    expect(r.ok).toBe(true);
    worldSummary = r.data as Record<string, unknown>;
    expect(worldSummary).toHaveProperty('scene');
    expect(worldSummary).toHaveProperty('has_active_action');
    expect(worldSummary).toHaveProperty('nearby_entity_count');
    console.log('[mvp] world summary:', JSON.stringify(worldSummary));
  });

  it('Step 2: get_nearby_entities — discover interactable entities', async () => {
    const r = await client.sendMCPRequest(req('get_nearby_entities', { radius: 20 }));
    // In Edit Mode without a scene the EntityRegistry may be empty — both outcomes valid
    if (r.ok) {
      const entities = (r.data as { entities: unknown[] }).entities;
      expect(Array.isArray(entities)).toBe(true);
      console.log('[mvp] nearby entities:', entities.length);
    } else {
      expect(r.error?.code).toMatch(/TARGET_NOT_FOUND|INVALID_PARAMS/);
    }
  });

  it('Step 3: get_inventory — confirm wrench is available', async () => {
    const r = await client.sendMCPRequest(req('get_inventory'));
    expect(r.ok).toBe(true);
    const items = (r.data as { items: Array<{ item_id: string }> }).items;
    const itemIds = items.map((i) => i.item_id);
    expect(itemIds).toContain('wrench');
  });

  it('Step 4: equip_item wrench — action returns ok=true with action_id', async () => {
    const r = await client.sendMCPRequest(req('equip_item', { item_id: 'wrench' }));
    expect(r.ok).toBe(true);
    // In Edit Mode we get accepted_edit_mode stub; in Play Mode we get a running action
    expect(r.action_id).toBeTruthy();
    expect(typeof r.action_id).toBe('string');
    console.log('[mvp] equip action_id:', r.action_id, 'status:', r.status);
  });

  it('Step 5: use_tool_on wrench + tv_01 — ok or TARGET_NOT_FOUND (entity may not exist in Edit Mode)', async () => {
    const r = await client.sendMCPRequest(
      req('use_tool_on', { tool_id: 'wrench', target_id: 'tv_01' })
    );
    // tv_01 may not exist in the current scene; both outcomes are valid
    if (r.ok) {
      expect(r.action_id).toBeTruthy();
    } else {
      expect(r.error?.code).toBe('TARGET_NOT_FOUND');
    }
    console.log('[mvp] use_tool_on result:', JSON.stringify(r));
  });

  it('Step 6: get_inventory again — Bridge state must not be corrupted by previous calls', async () => {
    const r = await client.sendMCPRequest(req('get_inventory'));
    expect(r.ok).toBe(true);
    const items = (r.data as { items: unknown[] }).items;
    expect(items.length).toBe(3);
  });
});
