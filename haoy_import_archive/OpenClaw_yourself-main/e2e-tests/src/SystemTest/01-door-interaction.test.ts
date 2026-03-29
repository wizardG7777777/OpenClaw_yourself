import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import { UnityBridgeClient } from '../client/UnityBridgeClient.js';

/**
 * System Test: Door Interaction (Door_Left / door_main)
 *
 * Validates the full Observe → Decide → Act cycle using the main room door.
 * Tests: move_to, interact_with, state query, has_active_action polling.
 *
 * Prerequisites: Run 00-preflight.test.ts first to verify environment.
 */

const client = new UnityBridgeClient();

beforeAll(async () => {
  console.log('\n=== SYSTEM TEST: Door Interaction ===\n');
  await client.connect();
});

afterAll(async () => {
  await client.disconnect();
});

// ──────────────────────────────────────────
//  Phase 1: Observe
// ──────────────────────────────────────────

describe('Phase 1: Observe — environment awareness', () => {
  let playerPos: Record<string, number>;
  let doorDistance: number;

  it('1.1 get_world_summary — read scene state', async () => {
    console.log('  → Observing world state...');
    const r = await client.call('get_world_summary');
    expect(r.ok).toBe(true);
    const data = r.data as Record<string, unknown>;
    console.log(`  ✔ Scene: ${data.scene}, entities: ${data.nearby_entity_count}, active_action: ${data.has_active_action}`);
  });

  it('1.2 get_nearby_entities — locate door_main', async () => {
    console.log('  → Scanning for door_main...');
    const r = await client.call('get_nearby_entities');
    expect(r.ok).toBe(true);
    const entities = (r.data as {
      entities: Array<{
        entity_id: string;
        display_name: string;
        distance: number;
        interactable: boolean;
        state?: Record<string, unknown>;
      }>;
    }).entities;

    const door = entities.find((e) => e.entity_id === 'door_main');
    expect(door).toBeDefined();
    doorDistance = door!.distance;

    // Log all entities for visibility
    for (const e of entities) {
      const stateStr = e.state ? ` [${Object.entries(e.state).map(([k, v]) => `${k}=${v}`).join(', ')}]` : '';
      console.log(`    - ${e.entity_id} (${e.display_name}) ${e.distance}m interactable=${e.interactable}${stateStr}`);
    }
    console.log(`  ✔ door_main found at ${doorDistance}m`);
  });

  it('1.3 get_player_state — record starting position', async () => {
    console.log('  → Reading player position...');
    const r = await client.call('get_player_state');
    expect(r.ok).toBe(true);
    const data = r.data as Record<string, unknown>;
    playerPos = data.position as Record<string, number>;
    console.log(`  ✔ Player at (${playerPos.x.toFixed(2)}, ${playerPos.y.toFixed(2)}, ${playerPos.z.toFixed(2)}), has_active_action=${data.has_active_action}`);
  });
});

// ──────────────────────────────────────────
//  Phase 2: Act — Move to door
// ──────────────────────────────────────────

describe('Phase 2: Act — move to door_main', () => {
  it('2.1 move_to door_main — command accepted', async () => {
    console.log('  → Sending: move_to door_main...');
    const r = await client.call('move_to', { target_id: 'door_main' });
    expect(r.ok).toBe(true);
    expect(r.action_id).toBeTruthy();
    expect(r.status).toBe('running');
    console.log(`  ✔ Movement started, action_id=${r.action_id}`);
  });

  it('2.2 wait for movement to complete', async () => {
    console.log('  → Waiting for MVPlayer to arrive at door...');
    const state = await client.waitForAction(3000, 10);
    const pos = state.position as Record<string, number>;
    console.log(`  ✔ Movement complete. Player at (${pos.x.toFixed(2)}, ${pos.y.toFixed(2)}, ${pos.z.toFixed(2)})`);
  });

  it('2.3 confirm distance to door < 3m', async () => {
    console.log('  → Verifying player is within interaction range...');
    const r = await client.call('get_nearby_entities');
    expect(r.ok).toBe(true);
    const entities = (r.data as {
      entities: Array<{ entity_id: string; distance: number }>;
    }).entities;

    const door = entities.find((e) => e.entity_id === 'door_main');
    expect(door).toBeDefined();
    expect(door!.distance).toBeLessThan(3);
    console.log(`  ✔ door_main distance = ${door!.distance}m (< 3m threshold)`);
  });
});

// ──────────────────────────────────────────
//  Phase 3: Act — Open door
// ──────────────────────────────────────────

describe('Phase 3: Act — interact with door (open)', () => {
  it('3.1 check door initial state', async () => {
    console.log('  → Reading door state before interaction...');
    const r = await client.call('get_nearby_entities');
    expect(r.ok).toBe(true);
    const entities = (r.data as {
      entities: Array<{ entity_id: string; state?: Record<string, unknown> }>;
    }).entities;
    const door = entities.find((e) => e.entity_id === 'door_main');
    const state = door?.state;
    console.log(`  ✔ Door state: ${state ? JSON.stringify(state) : 'no state (may be first run)'}`);
  });

  it('3.2 interact_with door_main — open the door', async () => {
    console.log('  → Sending: interact_with door_main (open)...');
    const r = await client.call('interact_with', { target_id: 'door_main' });
    expect(r.ok).toBe(true);
    expect(r.action_id).toBeTruthy();
    console.log(`  ✔ Interaction accepted, action_id=${r.action_id}`);
  });

  it('3.3 wait for door animation to finish', async () => {
    // Door animation takes ~0.5s, wait a bit then verify
    console.log('  → Waiting 2s for door animation...');
    await new Promise((r) => setTimeout(r, 2000));
    console.log('  ✔ Wait complete');
  });

  it('3.4 verify door state changed to "open"', async () => {
    console.log('  → Checking door state after opening...');
    const r = await client.call('get_nearby_entities');
    expect(r.ok).toBe(true);
    const entities = (r.data as {
      entities: Array<{ entity_id: string; state?: { status?: string; is_moving?: boolean } }>;
    }).entities;

    const door = entities.find((e) => e.entity_id === 'door_main');
    expect(door).toBeDefined();
    expect(door!.state).toBeDefined();
    expect(door!.state!.status).toBe('open');
    expect(door!.state!.is_moving).toBe(false);
    console.log(`  ✔ Door state: status=${door!.state!.status}, is_moving=${door!.state!.is_moving}`);
  });
});

// ──────────────────────────────────────────
//  Phase 4: Act — Close door
// ──────────────────────────────────────────

describe('Phase 4: Act — interact with door (close)', () => {
  it('4.1 interact_with door_main — close the door', async () => {
    console.log('  → Sending: interact_with door_main (close)...');
    const r = await client.call('interact_with', { target_id: 'door_main' });
    expect(r.ok).toBe(true);
    expect(r.action_id).toBeTruthy();
    console.log(`  ✔ Interaction accepted, action_id=${r.action_id}`);
  });

  it('4.2 wait for door animation to finish', async () => {
    console.log('  → Waiting 2s for door animation...');
    await new Promise((r) => setTimeout(r, 2000));
    console.log('  ✔ Wait complete');
  });

  it('4.3 verify door state changed to "closed"', async () => {
    console.log('  → Checking door state after closing...');
    const r = await client.call('get_nearby_entities');
    expect(r.ok).toBe(true);
    const entities = (r.data as {
      entities: Array<{ entity_id: string; state?: { status?: string; is_moving?: boolean } }>;
    }).entities;

    const door = entities.find((e) => e.entity_id === 'door_main');
    expect(door).toBeDefined();
    expect(door!.state).toBeDefined();
    expect(door!.state!.status).toBe('closed');
    expect(door!.state!.is_moving).toBe(false);
    console.log(`  ✔ Door state: status=${door!.state!.status}, is_moving=${door!.state!.is_moving}`);
  });
});

// ──────────────────────────────────────────
//  Phase 5: Verify — System integrity
// ──────────────────────────────────────────

describe('Phase 5: Verify — system integrity after full cycle', () => {
  it('5.1 no active action remaining', async () => {
    console.log('  → Checking system is idle...');
    const r = await client.call('get_player_state');
    expect(r.ok).toBe(true);
    const data = r.data as Record<string, unknown>;
    expect(data.has_active_action).toBe(false);
    console.log('  ✔ has_active_action = false');
  });

  it('5.2 all entities still intact', async () => {
    console.log('  → Verifying entity registry integrity...');
    const r = await client.call('get_nearby_entities', { interactable_only: false });
    expect(r.ok).toBe(true);
    const entities = (r.data as {
      entities: Array<{ entity_id: string; display_name: string }>;
    }).entities;

    // Should have at least: door_main, table_lamp, tv_01, npc_01, bed, sofa, office_table
    expect(entities.length).toBeGreaterThanOrEqual(4);

    console.log(`  ✔ ${entities.length} entities intact:`);
    for (const e of entities) {
      console.log(`    - ${e.entity_id} (${e.display_name})`);
    }
  });

  it('5.3 world summary consistent', async () => {
    console.log('  → Final world state check...');
    const r = await client.call('get_world_summary');
    expect(r.ok).toBe(true);
    const data = r.data as Record<string, unknown>;
    expect(data.has_active_action).toBe(false);
    expect(typeof data.nearby_entity_count).toBe('number');
    console.log(`  ✔ Scene: ${data.scene}, entities: ${data.nearby_entity_count}, active_action: ${data.has_active_action}`);
  });
});
