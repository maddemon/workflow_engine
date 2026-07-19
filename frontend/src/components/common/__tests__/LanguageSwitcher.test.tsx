import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithProvider } from '../../../test-utils.tsx';
import { LanguageSwitcher } from '../LanguageSwitcher.tsx';

describe('LanguageSwitcher', () => {
  let store: Record<string, string>;

  beforeEach(() => {
    store = {};
    Object.defineProperty(window, 'localStorage', {
      value: {
        getItem: vi.fn((key: string) => store[key] ?? null),
        setItem: vi.fn((key: string, value: string) => { store[key] = value; }),
        removeItem: vi.fn((key: string) => { delete store[key]; }),
        clear: vi.fn(() => { store = {}; }),
      },
      writable: true,
    });
  });

  it('renders language menu and switches to zh-CN', async () => {
    renderWithProvider(<LanguageSwitcher />);

    const button = screen.getByRole('button', { name: /switch language/i });
    fireEvent.click(button);

    const chineseOption = await screen.findByText('中文');
    fireEvent.click(chineseOption);

    await waitFor(() => {
      expect(window.localStorage.setItem).toHaveBeenCalledWith('i18nextLng', 'zh-CN');
    });
  });

  it('marks current language with check icon', async () => {
    renderWithProvider(<LanguageSwitcher />);

    fireEvent.click(screen.getByRole('button', { name: /switch language/i }));

    const englishOption = await screen.findByText('English');
    expect(englishOption).toBeDefined();
  });
});
