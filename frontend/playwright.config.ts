import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir:'./tests', testIgnore:process.env.CLARY_LIVE_TESTS==='1'?[]:['**/live-*.spec.ts','**/conversation.spec.ts','**/uploads.spec.ts'], workers:1, timeout:90000, use:{ viewport:{width:1670,height:941}, headless:true }, reporter:'list' });

