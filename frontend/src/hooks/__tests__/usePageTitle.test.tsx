import { afterEach, describe, expect, it } from 'vitest';
import { renderWithProvider } from '../../test-utils.tsx';
import { usePageTitle } from '../usePageTitle.ts';
import { useWorkflowStore } from '../../stores/workflowStore.ts';

function TitleProbe() {
  usePageTitle();
  return null;
}

describe('usePageTitle', () => {
  afterEach(() => {
    useWorkflowStore.setState({ workflowName: '' });
    document.title = 'Flow Engine';
  });

  it('sets the workflows title on the list route', () => {
    renderWithProvider(<TitleProbe />, { withRouter: true, initialEntries: ['/'] });
    expect(document.title).toBe('Workflows · Flow Engine');
  });

  it('sets the execution history title', () => {
    renderWithProvider(<TitleProbe />, { withRouter: true, initialEntries: ['/workflow/123/history'] });
    expect(document.title).toBe('Execution History · Flow Engine');
  });

  it('uses the workflow name on the editor route', () => {
    useWorkflowStore.setState({ workflowName: 'My Flow' });
    renderWithProvider(<TitleProbe />, { withRouter: true, initialEntries: ['/workflow/123'] });
    expect(document.title).toBe('My Flow · Flow Engine');
  });

  it('falls back to the editor title when no workflow name is set', () => {
    renderWithProvider(<TitleProbe />, { withRouter: true, initialEntries: ['/workflow/123'] });
    expect(document.title).toBe('Workflow Editor · Flow Engine');
  });

  it('uses admin section titles', () => {
    renderWithProvider(<TitleProbe />, { withRouter: true, initialEntries: ['/admin/audit'] });
    expect(document.title).toBe('Audit Log · Flow Engine');
  });

  it('falls back to the app name on unknown routes', () => {
    renderWithProvider(<TitleProbe />, { withRouter: true, initialEntries: ['/unknown'] });
    expect(document.title).toBe('Flow Engine');
  });
});
