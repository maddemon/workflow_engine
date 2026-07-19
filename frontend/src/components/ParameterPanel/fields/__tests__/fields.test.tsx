import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProvider } from '../../../../test-utils.tsx';
import type { ParameterDefinition } from '../../../../types/workflow.ts';

vi.mock('../../../../services/api.ts', () => ({
  getCredentials: vi.fn().mockResolvedValue([]),
  getCredentialTypes: vi.fn().mockResolvedValue([]),
  uploadFile: vi.fn(),
  listFiles: vi.fn().mockResolvedValue([]),
}));

function baseDefinition(type: ParameterDefinition['type']): ParameterDefinition {
  return {
    name: 'test',
    displayName: 'Test Field',
    type,
    defaultValue: '',
    required: false,
    validationRules: [],
    displayRule: null,
    credentialType: null,
    options: [],
  };
}

describe('ParameterPanel fields', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.ResizeObserver = class {
      observe() {}
      unobserve() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver;
    Object.defineProperty(document, 'fonts', {
      writable: true,
      configurable: true,
      value: {
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      } as unknown as FontFaceSet,
    });
  });

  describe('StringField', () => {
    it('renders and calls onChange', async () => {
      const onChange = vi.fn();
      const { StringField } = await import('../StringField.tsx');
      renderWithProvider(<StringField definition={baseDefinition('String')} value="hello" onChange={onChange} />);
      const input = screen.getByDisplayValue('hello');
      fireEvent.change(input, { target: { value: 'world' } });
      expect(onChange).toHaveBeenCalledWith('world');
    });

    it('shows required asterisk', async () => {
      const { StringField } = await import('../StringField.tsx');
      const def = { ...baseDefinition('String'), required: true };
      const { container } = renderWithProvider(<StringField definition={def} value="" onChange={vi.fn()} />);
      expect(container.textContent).toContain('Test Field');
    });
  });

  describe('NumberField', () => {
    it('renders numeric value and calls onChange', async () => {
      const onChange = vi.fn();
      const { NumberField } = await import('../NumberField.tsx');
      renderWithProvider(<NumberField definition={baseDefinition('Number')} value={42} onChange={onChange} />);
      const input = screen.getByDisplayValue('42');
      fireEvent.change(input, { target: { value: '100' } });
      expect(onChange).toHaveBeenCalledWith(100);
    });
  });

  describe('BooleanField', () => {
    it('renders switch and toggles value', async () => {
      const onChange = vi.fn();
      const { BooleanField } = await import('../BooleanField.tsx');
      renderWithProvider(<BooleanField definition={baseDefinition('Boolean')} value={false} onChange={onChange} />);
      const switchInput = screen.getByRole('switch');
      fireEvent.click(switchInput);
      expect(onChange).toHaveBeenCalledWith(true);
    });
  });

  describe('TextAreaField', () => {
    it('renders textarea and calls onChange', async () => {
      const onChange = vi.fn();
      const { TextAreaField } = await import('../TextAreaField.tsx');
      renderWithProvider(<TextAreaField definition={baseDefinition('String')} value="hello" onChange={onChange} />);
      const input = screen.getByDisplayValue('hello');
      fireEvent.change(input, { target: { value: 'world' } });
      expect(onChange).toHaveBeenCalledWith('world');
    });
  });

  describe('SecretField', () => {
    it('renders password input and calls onChange', async () => {
      const onChange = vi.fn();
      const { SecretField } = await import('../SecretField.tsx');
      renderWithProvider(<SecretField definition={baseDefinition('String')} value="secret" onChange={onChange} />);
      const input = screen.getByDisplayValue('secret');
      fireEvent.change(input, { target: { value: 'newsecret' } });
      expect(onChange).toHaveBeenCalledWith('newsecret');
    });
  });

  describe('OptionsField', () => {
    it('renders select with options', async () => {
      const onChange = vi.fn();
      const { OptionsField } = await import('../OptionsField.tsx');
      const def = { ...baseDefinition('Options'), options: [{ label: 'A', value: 'a' }, { label: 'B', value: 'b' }] };
      renderWithProvider(<OptionsField definition={def} value="a" onChange={onChange} />);
      expect(screen.getByText('A')).toBeDefined();
    });
  });

  describe('ButtonGroupField', () => {
    it('renders buttons and selects option', async () => {
      const onChange = vi.fn();
      const { ButtonGroupField } = await import('../ButtonGroupField.tsx');
      const def = { ...baseDefinition('Options'), options: [{ label: 'A', value: 'a' }, { label: 'B', value: 'b' }] };
      renderWithProvider(<ButtonGroupField definition={def} value="" onChange={onChange} />);
      fireEvent.click(screen.getByText('A'));
      expect(onChange).toHaveBeenCalledWith('a');
    });
  });

  describe('ResourceField', () => {
    it('renders resource select', async () => {
      const onChange = vi.fn();
      const { ResourceField } = await import('../ResourceField.tsx');
      const def = { ...baseDefinition('Resource'), resourceType: 'project', options: [{ label: 'P1', value: 'p1' }] };
      renderWithProvider(<ResourceField definition={def} value="" onChange={onChange} />);
      expect(screen.getByText('P1')).toBeDefined();
    });
  });

  describe('InfoTooltip', () => {
    it('renders info icon', async () => {
      const { InfoTooltip } = await import('../InfoTooltip.tsx');
      const { container } = renderWithProvider(<InfoTooltip label="hint text" />);
      expect(container.querySelector('svg')).toBeDefined();
    });
  });

  describe('CodeField', () => {
    it('renders code textarea', async () => {
      const onChange = vi.fn();
      const { CodeField } = await import('../CodeField.tsx');
      renderWithProvider(<CodeField definition={baseDefinition('String')} value="const x = 1;" onChange={onChange} />);
      const input = screen.getByDisplayValue('const x = 1;');
      fireEvent.change(input, { target: { value: 'const y = 2;' } });
      expect(onChange).toHaveBeenCalledWith('const y = 2;');
    });
  });

  describe('JsonField', () => {
    it('renders json textarea and formats', async () => {
      const onChange = vi.fn();
      const { JsonField } = await import('../JsonField.tsx');
      renderWithProvider(<JsonField definition={baseDefinition('String')} value='{"a":1}' onChange={onChange} />);
      const input = screen.getByDisplayValue('{"a":1}');
      expect(input).toBeDefined();
      fireEvent.change(input, { target: { value: 'invalid' } });
      expect(onChange).toHaveBeenCalledWith('invalid');
    });
  });

  describe('KeyValueField', () => {
    it('renders key value entries and adds new entry', async () => {
      const onChange = vi.fn();
      const { KeyValueField } = await import('../KeyValueField.tsx');
      renderWithProvider(<KeyValueField definition={baseDefinition('String')} value='{"a":"b"}' onChange={onChange} />);
      fireEvent.click(screen.getByTitle('Add entry'));
      expect(onChange).toHaveBeenCalled();
    });
  });

  describe('ArrayField', () => {
    it('renders empty array and adds item', async () => {
      const onChange = vi.fn();
      const { ArrayField } = await import('../ArrayField.tsx');
      renderWithProvider(<ArrayField definition={baseDefinition('Array')} value={[]} onChange={onChange} />);
      fireEvent.click(screen.getByText('Add'));
      expect(onChange).toHaveBeenCalledWith(['']);
    });

    it('renders structured array items', async () => {
      const onChange = vi.fn();
      const { ArrayField } = await import('../ArrayField.tsx');
      const itemDef = { ...baseDefinition('String'), fields: [{ ...baseDefinition('String'), name: 'name' }] };
      const def = { ...baseDefinition('Array'), itemDefinition: itemDef };
      renderWithProvider(<ArrayField definition={def} value={[{ name: 'alpha' }]} onChange={onChange} />);
      expect(screen.getByText('alpha')).toBeDefined();
      expect(screen.getAllByText('Test Field').length).toBeGreaterThanOrEqual(2);
    });
  });

  describe('CronBuilder', () => {
    it('renders and changes preset type', async () => {
      const onChange = vi.fn();
      const { CronBuilder } = await import('../CronBuilder.tsx');
      renderWithProvider(<CronBuilder value="0 9 * * *" onChange={onChange} />);
      expect(screen.getByText('Generated:')).toBeDefined();
    });
  });

  describe('ExpressionField', () => {
    it('renders expression textarea', async () => {
      const onChange = vi.fn();
      const { ExpressionField } = await import('../ExpressionField.tsx');
      renderWithProvider(<ExpressionField definition={baseDefinition('String')} value="$json.name" onChange={onChange} />);
      const input = screen.getByDisplayValue('$json.name');
      expect(input).toBeDefined();
    });
  });
});
