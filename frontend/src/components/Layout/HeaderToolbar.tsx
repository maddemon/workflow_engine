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
import { Bell, BookOpen, Home, LogOut, Moon, Settings, Shield, Sun, User } from "lucide-react"
import { useMemo } from "react"
import { useRequest } from "ahooks"
import { useTranslation } from "react-i18next"
import { Link, useLocation, useNavigate } from "react-router-dom"
import { CredentialMenu } from "../CredentialPanel/CredentialMenu.tsx"
import { LanguageSwitcher } from "../common/LanguageSwitcher.tsx"
import { useAuth } from "../../hooks/AuthContext.tsx"
import { useRoles } from "../../hooks/useRoles.ts"
import { getWorkflows } from "../../services/api.ts"

const navItems = [
  { label: "workflows", icon: Home, path: "/" },
  { label: "documents", icon: BookOpen, path: "/help" },
]

const adminNavItems = [
  { label: "userManagement", icon: Shield, path: "/admin/users" },
  { label: "projectClassification", icon: Shield, path: "/admin/projects" },
  { label: "auditLog", icon: Shield, path: "/admin/audit" },
  { label: "fileManagement", icon: Shield, path: "/admin/files" },
]

export function HeaderToolbar() {
  const colorScheme = useComputedColorScheme("light")
  const { toggleColorScheme } = useMantineColorScheme()
  const location = useLocation()
  const navigate = useNavigate()
  const { user, logout } = useAuth()
  const { hasRole } = useRoles()
  const { t } = useTranslation("header")
  const { data: workflows = [] } = useRequest(getWorkflows, {
    pollingInterval: 60000,
  });

  const pendingAiDrafts = useMemo(
    () => workflows.filter((w) => w.source === 'Ai' && w.draftStatus === 'Pending').length,
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
              <img src="/favicon.svg" alt="FlowEngine" width={20} height={20} />
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
                    <Text size="xs">{t("system")}</Text>
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
                    {t(item.label)}
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
                  <Text size="xs">{t(item.label)}</Text>
                </Group>
              </Anchor>
            )
          })}
        </Group>

        <Group gap={4} wrap="nowrap">
          <CredentialMenu />
          <Tooltip label={t(colorScheme === "dark" ? "switchToLightMode" : "switchToDarkMode")}>
            <ActionIcon
              variant="subtle"
              color="gray"
              size="sm"
              onClick={toggleColorScheme}
              aria-label={t("toggleColorScheme")}
            >
              {colorScheme === "dark" ? <Sun size={16} /> : <Moon size={16} />}
            </ActionIcon>
          </Tooltip>
          <ActionIcon variant="subtle" color="gray" size="sm" aria-label={t("notifications")}>
            <Bell size={16} />
          </ActionIcon>
          <LanguageSwitcher />
          <Menu shadow="md" width={180}>
            <Menu.Target>
              <ActionIcon variant="subtle" color="gray" size="lg" radius="sm" aria-label={t("menu")}>
                <Avatar size={24} radius="sm" color="brand-blue" variant="filled">
                  {user?.displayName?.[0]?.toUpperCase() ?? <User size={14} />}
                </Avatar>
              </ActionIcon>
            </Menu.Target>
              <Menu.Dropdown>
              <Text size="xs" px="sm" py={4} c="dimmed" ta="center">
                {user?.email ?? t("notSignedIn")}
              </Text>
              <Menu.Divider />
              <Menu.Item leftSection={<Settings size={14} />} onClick={() => navigate('/settings')}>
                {t("settings")}
              </Menu.Item>
              <Menu.Item leftSection={<LogOut size={14} />} color="red" onClick={handleLogout}>
                {t("logout")}
              </Menu.Item>
            </Menu.Dropdown>
          </Menu>
        </Group>
      </Box>
    </>
  )
}
