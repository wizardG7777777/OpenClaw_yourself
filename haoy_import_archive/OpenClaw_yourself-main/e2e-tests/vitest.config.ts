import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    pool: 'forks',
    poolOptions: { forks: { singleFork: true } },
    testTimeout: 60_000,
    hookTimeout: 30_000,
    sequence: { sequential: true },
    globalSetup: './src/globalSetup.ts',
  },
});
