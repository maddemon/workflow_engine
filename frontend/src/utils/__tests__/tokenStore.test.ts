import { describe, it, expect } from 'vitest';
import { tokenStore } from '../tokenStore.ts';

describe('tokenStore', () => {
  it('getToken_initialState_returnsNull', () => {
    expect(tokenStore.getToken()).toBeNull();
  });

  it('setToken_validToken_storesToken', () => {
    tokenStore.setToken('abc123');
    expect(tokenStore.getToken()).toBe('abc123');
  });

  it('setToken_nullValue_clearsToken', () => {
    tokenStore.setToken('abc123');
    tokenStore.setToken(null);
    expect(tokenStore.getToken()).toBeNull();
  });

  it('clear_afterSet_resetsToken', () => {
    tokenStore.setToken('xyz');
    tokenStore.clear();
    expect(tokenStore.getToken()).toBeNull();
  });
});
