import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { UnityBridgeClient } from '../client/UnityBridgeClient.js';

/**
 * Preflight checks — verifies all prerequisites before running system tests.
 * Must pass before any other SystemTest files run.
 */

const client = new UnityBridgeClient();

beforeAll(async () => {
  console.log('\n=== PREFLIGHT: Checking MCP prerequisites ===\n');
  await client.connect();
});

afterAll(async () => {
  await client.disconnect();
});

describe('Preflight — MCP connection', () => {
  it('Unity MCP server is reachable', () => {
    // If beforeAll succeeded, connection is established
    console.log('  ✔ MCP server connected at http://127.0.0.1:8080/mcp');
    expect(true).toBe(true);
  });
});

describe('Preflight — Play Mode required', () => {
  it('get_player_state returns player data (Play Mode active)', async () => {
    console.log('  → Checking: Unity is in Play Mode with Player in scene...');
    const r = await client.call('get_player_state');
    if (!r.ok) {
      console.error('  ✘ Player not found. Is Unity in Play Mode with MVPlayer (tag=Player) in the scene?');
    }
    expect(r.ok).toBe(true);
    const data = r.data as Record<string, unknown>;
    expect(data).toHaveProperty('position');
    expect(data).toHaveProperty('scene');
    console.log(`  ✔ Player found in scene "${data.scene}" at position:`, data.position);
  });
});

describe('Preflight — EntityRegistry active', () => {
  it('get_nearby_entities returns entity list', async () => {
    console.log('  → Checking: EntityRegistry is initialized...');
    const r = await client.call('get_nearby_entities');
    expect(r.ok).toBe(true);
    const entities = (r.data as { entities: Array<{ entity_id: string }> }).entities;
    expect(Array.isArray(entities)).toBe(true);
    console.log(`  ✔ EntityRegistry active, ${entities.length} entities found`);
  });
});

describe('Preflight — door_main entity exists', () => {
  it('door_main is in the entity list and interactable', async () => {
    console.log('  → Checking: door_main entity is configured...');
    const r = await client.call('get_nearby_entities');
    expect(r.ok).toBe(true);
    const entities = (r.data as {
      entities: Array<{ entity_id: string; interactable: boolean; distance: number }>;
    }).entities;

    const door = entities.find((e) => e.entity_id === 'door_main');
    if (!door) {
      console.error('  ✘ door_main not found. Add EntityIdentity (entity_id="door_main") to Door_Left in the scene.');
    }
    expect(door).toBeDefined();
    expect(door!.interactable).toBe(true);
    console.log(`  ✔ door_main found, distance=${door!.distance}m, interactable=true`);
  });
});

describe('Preflight — MCP tools available', () => {
  it('all 4 query tools respond', async () => {
    console.log('  → Checking: Query tools...');
    const tools = ['get_player_state', 'get_world_summary', 'get_nearby_entities', 'get_inventory'];
    for (const tool of tools) {
      const r = await client.call(tool);
      expect(r.ok).toBe(true);
      console.log(`    ✔ ${tool} — ok`);
    }
  });

  it('move_to rejects missing target_id (validates action tool routing)', async () => {
    console.log('  → Checking: Action tool validation...');
    const r = await client.call('move_to', {});
    expect(r.ok).toBe(false);
    expect(r.error?.code).toBe('INVALID_PARAMS');
    console.log('    ✔ move_to correctly rejects missing target_id');
  });
});

describe('Preflight — has_active_action works', () => {
  it('has_active_action is false when idle', async () => {
    console.log('  → Checking: has_active_action polling mechanism...');
    const r = await client.call('get_player_state');
    expect(r.ok).toBe(true);
    const data = r.data as Record<string, unknown>;
    expect(data.has_active_action).toBe(false);
    console.log('  ✔ has_active_action = false (idle state confirmed)');
  });
});
