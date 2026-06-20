import { useState } from 'react';
import { ActionIcon, Tooltip, Text, useComputedColorScheme, useMantineColorScheme, Avatar, Menu, Box, Anchor } from '@mantine/core';
import { Workflow, Sun, Moon, User, Bell, Key, Home, Settings, BarChart3 } from 'lucide-react';
import { Link, useLocation } from 'react-router-dom';
import { CredentialListModal } from '../CredentialPanel/CredentialListModal.tsx';

const navItems = [
  { label: 'Workflows', icon: Home, path: '/' },
  { label: 'Executions', icon: BarChart3, path: '/executions' },
  { label: 'Settings', icon: Settings, path: '/settings' },
];

export function HeaderToolbar() {
  const [credModalOpen, setCredModalOpen] = useState(false);
  const colorScheme = useComputedColorScheme('light');
  const { toggleColorScheme } = useMantineColorScheme();
  const location = useLocation();

  return (
    <>
      <Box component="header" className="app-header">
        <Box className="header-left">
          <Anchor component={Link} to="/" underline="never" className="header-logo">
            <Workflow size={18} style={{ color: 'var(--mantine-color-blue-6)' }} />
            <Text fw={700} size="sm" style={{ letterSpacing: '-0.02em', color: 'inherit' }}>FlowEngine</Text>
          </Anchor>
          <Box className="header-divider" />
          {navItems.map((item) => {
            const active = item.path === '/'
              ? location.pathname === '/' || location.pathname.startsWith('/workflow')
              : location.pathname.startsWith(item.path);
            return (
              <Anchor
                key={item.path}
                component={Link}
                to={item.path}
                underline="never"
                className={`nav-item${active ? ' active' : ''}`}
              >
                <Box className="nav-item-inner">
                  <item.icon size={13} />
                  <Text size="xs">{item.label}</Text>
                </Box>
              </Anchor>
            );
          })}
        </Box>

        <Box className="header-right">
          <Tooltip label="Manage Credentials">
            <ActionIcon variant="subtle" color="gray" size="sm" onClick={() => setCredModalOpen(true)} aria-label="Credentials">
              <Key size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label={`Switch to ${colorScheme === 'dark' ? 'light' : 'dark'} mode`}>
            <ActionIcon variant="subtle" color="gray" size="sm" onClick={toggleColorScheme} aria-label="Toggle color scheme">
              {colorScheme === 'dark' ? <Sun size={16} /> : <Moon size={16} />}
            </ActionIcon>
          </Tooltip>
          <ActionIcon variant="subtle" color="gray" size="sm" aria-label="Notifications">
            <Bell size={16} />
          </ActionIcon>
          <Menu shadow="md" width={180}>
            <Menu.Target>
              <ActionIcon variant="subtle" color="gray" size="lg" radius="sm" aria-label="Menu">
                <Avatar size={24} radius="sm" color="blue" variant="filled">
                  <User size={14} />
                </Avatar>
              </ActionIcon>
            </Menu.Target>
            <Menu.Dropdown>
              <Menu.Item>Profile</Menu.Item>
              <Menu.Item>Settings</Menu.Item>
              <Menu.Divider />
              <Menu.Item color="red">Logout</Menu.Item>
            </Menu.Dropdown>
          </Menu>
        </Box>
      </Box>
      <CredentialListModal opened={credModalOpen} onClose={() => setCredModalOpen(false)} />
    </>
  );
}
