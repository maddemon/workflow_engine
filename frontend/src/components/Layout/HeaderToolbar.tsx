import {
  ActionIcon,
  Anchor,
  Avatar,
  Badge,
  Box,
  Divider,
  Flex,
  Group,
  Menu,
  Text,
  Tooltip,
  useComputedColorScheme,
  useMantineColorScheme,
} from "@mantine/core"
import { Bell, BookOpen, Home, Key, LogOut, Moon, Settings, Shield, Sun, User, Workflow } from "lucide-react"
import { useMemo, useState } from "react"
import { useRequest } from "ahooks"
import { Link, useLocation, useNavigate } from "react-router-dom"
import { CredentialListModal } from "../CredentialPanel/CredentialListModal.tsx"
import { useAuth } from "../../hooks/AuthContext.tsx"
import { useRoles } from "../../hooks/useRoles.ts"
import { getWorkflows } from "../../services/api.ts"

const navItems = [
  { label: "Workflows", icon: Home, path: "/" },
  { label: "Documents", icon: BookOpen, path: "/help" },
]

const adminNavItems = [
  { label: "User Management", icon: Shield, path: "/admin/users" },
  { label: "Project Classification", icon: Shield, path: "/admin/projects" },
  { label: "Audit Log", icon: Shield, path: "/admin/audit" },
  { label: "File Management", icon: Shield, path: "/admin/files" },
]

export function HeaderToolbar() {
  const [credModalOpen, setCredModalOpen] = useState(false)
  const colorScheme = useComputedColorScheme("light")
  const { toggleColorScheme } = useMantineColorScheme()
  const location = useLocation()
  const navigate = useNavigate()
  const { user, logout } = useAuth()
  const { hasRole } = useRoles()
  const { data: workflows = [] } = useRequest(getWorkflows, {
    pollingInterval: 60000,
  });

  const pendingAiDrafts = useMemo(
    () => workflows.filter((w) => w.source === 'ai' && w.draftStatus === 'pending').length,
    [workflows],
  );

  const handleLogout = async () => {
    await logout()
    navigate("/login")
  }

  return (
    <>
      <Box component="header" className="app-header">
        <Group>
          <Anchor component={Link} to="/" underline="never">
            <Flex gap={4} align="center" wrap="nowrap">
              <Workflow size={18} />
              <Text fw={700} size="sm">
                WorkFlow Engine
              </Text>
              <Badge size="xs">Beta</Badge>
            </Flex>
          </Anchor>
          <Divider orientation="vertical" />
          {hasRole('Admin') && (
            <Menu shadow="md" width={180} trigger="hover" openDelay={100}>
              <Menu.Target>
                <Anchor
                  component="button"
                  underline="never"
                  className={`nav-item${location.pathname.startsWith('/admin/') ? ' active' : ''}`}
                >
                  <Group gap={4} wrap="nowrap">
                    <Shield size={13} />
                    <Text size="xs">System</Text>
                    {pendingAiDrafts > 0 && (
                      <Badge size="xs" variant="filled" color="red">{pendingAiDrafts}</Badge>
                    )}
                  </Group>
                </Anchor>
              </Menu.Target>
              <Menu.Dropdown>
                {adminNavItems.map((item) => (
                  <Menu.Item
                    key={item.path}
                    leftSection={<item.icon size={14} />}
                    onClick={() => navigate(item.path)}
                  >
                    {item.label}
                  </Menu.Item>
                ))}
              </Menu.Dropdown>
            </Menu>
          )}
          {navItems.map((item) => {
            const active =
              item.path === "/"
                ? location.pathname === "/" || location.pathname.startsWith("/workflow/")
                : location.pathname === item.path || location.pathname.startsWith(item.path + "/")
            return (
              <Anchor
                key={item.path}
                component={Link}
                to={item.path}
                underline="never"
                className={`nav-item${active ? " active" : ""}`}
              >
                <Group gap={4} wrap="nowrap">
                  <item.icon size={13} />
                  <Text size="xs">{item.label}</Text>
                </Group>
              </Anchor>
            )
          })}
        </Group>

        <Group gap={4} wrap="nowrap">
          <Tooltip label="Manage Credentials">
            <ActionIcon
              variant="subtle"
              color="gray"
              size="sm"
              onClick={() => setCredModalOpen(true)}
              aria-label="Credentials"
            >
              <Key size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label={`Switch to ${colorScheme === "dark" ? "light" : "dark"} mode`}>
            <ActionIcon
              variant="subtle"
              color="gray"
              size="sm"
              onClick={toggleColorScheme}
              aria-label="Toggle color scheme"
            >
              {colorScheme === "dark" ? <Sun size={16} /> : <Moon size={16} />}
            </ActionIcon>
          </Tooltip>
          <ActionIcon variant="subtle" color="gray" size="sm" aria-label="Notifications">
            <Bell size={16} />
          </ActionIcon>
          <Menu shadow="md" width={180}>
            <Menu.Target>
              <ActionIcon variant="subtle" color="gray" size="lg" radius="sm" aria-label="Menu">
                <Avatar size={24} radius="sm" color="brand-blue" variant="filled">
                  {user?.displayName?.[0]?.toUpperCase() ?? <User size={14} />}
                </Avatar>
              </ActionIcon>
            </Menu.Target>
              <Menu.Dropdown>
              <Text size="xs" px="sm" py={4} c="dimmed" ta="center">
                {user?.email ?? "Not signed in"}
              </Text>
              <Menu.Divider />
              <Menu.Item leftSection={<Settings size={14} />} onClick={() => navigate('/settings')}>
                Settings
              </Menu.Item>
              <Menu.Item leftSection={<LogOut size={14} />} color="red" onClick={handleLogout}>
                Logout
              </Menu.Item>
            </Menu.Dropdown>
          </Menu>
        </Group>
      </Box>
      <CredentialListModal opened={credModalOpen} onClose={() => setCredModalOpen(false)} />
    </>
  )
}
