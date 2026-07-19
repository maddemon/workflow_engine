import '@testing-library/jest-dom/vitest';

if (typeof window !== 'undefined' && !window.ResizeObserver) {
  class ResizeObserverMock {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  window.ResizeObserver = ResizeObserverMock as unknown as typeof ResizeObserver;
}

if (typeof window !== 'undefined' && !window.DOMMatrixReadOnly) {
  class DOMMatrixReadOnlyMock {
    a: number;
    b: number;
    c: number;
    d: number;
    e: number;
    f: number;
    constructor(a = 1, b = 0, c = 0, d = 1, e = 0, f = 0) {
      this.a = a;
      this.b = b;
      this.c = c;
      this.d = d;
      this.e = e;
      this.f = f;
    }
    multiply() { return this; }
    translate() { return this; }
    scale() { return this; }
    rotate() { return this; }
    inverse() { return this; }
    transformPoint(point: { x: number; y: number }) { return point; }
    toString() { return `matrix(${this.a}, ${this.b}, ${this.c}, ${this.d}, ${this.e}, ${this.f})`; }
  }
  window.DOMMatrixReadOnly = DOMMatrixReadOnlyMock as unknown as typeof window.DOMMatrixReadOnly;
}

if (typeof window !== 'undefined' && !window.matchMedia) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  });
}
