import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { UnityBridgeClient, type MCPGameResponse } from '../client/UnityBridgeClient.js';

const client = new UnityBridgeClient();

beforeAll(async () => {
  await client.connect(process.env.MCP_URL ?? process.env.MCP_WS_URL ?? 'http://127.0.0.1:8080/mcp');
});

afterAll(async () => {
  await client.disconnect();
});

// Helper: build a minimal valid MCP request JSON
function req(tool: string, args: Record<string, unknown> = {}): string {
  return JSON.stringify({ tool, args });
}

describe('get_inventory', () => {
  it('returns ok=true with 3 MVP items', async () => {
    const r = await client.sendMCPRequest(req('get_inventory'));
    expect(r.ok).toBe(true);
    expect(r.data).toBeDefined();
    const items = (r.data as { items: unknown[] }).items;
    expect(Array.isArray(items)).toBe(true);
    expect(items.length).toBe(3);
    const ids = items.map((i: unknown) => (i as { item_id: string }).item_id);
    expect(ids).toContain('wrench');
    expect(ids).toContain('shovel');
    expect(ids).toContain('postcard');
    // Each item must have display_name and quantity
    for (const item of items as Array<{ item_id: string; display_name: string; quantity: number }>) {
      expect(typeof item.display_name).toBe('string');
      expect(typeof item.quantity).toBe('number');
    }
  });

  it('works without args field (RequestValidator injects empty {})', async () => {
    const r = await client.sendMCPRequest(JSON.stringify({ tool: 'get_inventory' }));
    expect(r.ok).toBe(true);
  });
});

describe('get_player_state', () => {
  it('returns ok=true with position/rotation/scene, or TARGET_NOT_FOUND when no Player tag', async () => {
    const r = await client.sendMCPRequest(req('get_player_state'));
    if (r.ok) {
      const d = r.data as Record<string, unknown>;
      expect(d).toHaveProperty('position');
      expect(d).toHaveProperty('rotation');
      expect(d).toHaveProperty('scene');
    } else {
      expect(r.error?.code).toBe('TARGET_NOT_FOUND');
    }
  });
});

describe('get_nearby_entities', () => {
  it('returns ok=true with entities array, or ok=false with TARGET_NOT_FOUND', async () => {
    const r = await client.sendMCPRequest(req('get_nearby_entities', { radius: 20 }));
    if (r.ok) {
      const d = r.data as Record<string, unknown>;
      expect(Array.isArray(d.entities)).toBe(true);
    } else {
      // Acceptable in Edit Mode with no scene loaded
      expect(r.error?.code).toMatch(/TARGET_NOT_FOUND|INVALID_PARAMS/);
    }
  });
});

describe('get_world_summary', () => {
  it('returns ok=true with expected summary fields', async () => {
    const r = await client.sendMCPRequest(req('get_world_summary'));
    expect(r.ok).toBe(true);
    const d = r.data as Record<string, unknown>;
    expect(d).toHaveProperty('scene');
    expect(d).toHaveProperty('has_active_action');
    expect(d).toHaveProperty('nearby_entity_count');
    expect(typeof d.has_active_action).toBe('boolean');
    expect(typeof d.nearby_entity_count).toBe('number');
  });
});
