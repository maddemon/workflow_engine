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
import { BarChart3, Bell, Home, Key, Moon, Settings, Sun, User, Workflow } from "lucide-react"
import { useState } from "react"
import { Link, useLocation } from "react-router-dom"
import { CredentialListModal } from "../CredentialPanel/CredentialListModal.tsx"

const navItems = [
  { label: "Workflows", icon: Home, path: "/" },
  { label: "Executions", icon: BarChart3, path: "/executions" },
  { label: "Settings", icon: Settings, path: "/settings" },
]

export function HeaderToolbar() {
  const [credModalOpen, setCredModalOpen] = useState(false)
  const colorScheme = useComputedColorScheme("light")
  const { toggleColorScheme } = useMantineColorScheme()
  const location = useLocation()

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
          {navItems.map((item) => {
            const active =
              item.path === "/"
                ? location.pathname === "/" || location.pathname.startsWith("/workflow")
                : location.pathname.startsWith(item.path)
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
        </Group>
      </Box>
      <CredentialListModal opened={credModalOpen} onClose={() => setCredModalOpen(false)} />
    </>
  )
}
