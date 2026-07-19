import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { NodeIcon } from '../NodeIcon.tsx';

describe('NodeIcon', () => {
  it('renders a known icon', () => {
    const { container } = render(<NodeIcon icon="globe" />);
    expect(container.querySelector('svg')).toBeDefined();
  });

  it('renders fallback box icon for unknown icon', () => {
    const { container } = render(<NodeIcon icon="unknown-icon" />);
    expect(container.querySelector('svg')).toBeDefined();
  });

  it('is case-insensitive for icon names', () => {
    const { container } = render(<NodeIcon icon="DATABASE" />);
    expect(container.querySelector('svg')).toBeDefined();
  });

  it('applies custom size and color', () => {
    const { container } = render(<NodeIcon icon="play" size={24} color="#f00" />);
    const svg = container.querySelector('svg');
    expect(svg).toBeDefined();
    expect(svg).toHaveAttribute('width', '24');
    expect(svg).toHaveAttribute('height', '24');
    expect(svg).toHaveAttribute('stroke', '#f00');
  });
});
