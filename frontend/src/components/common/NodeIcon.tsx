import {
  Globe,
  GitBranch,
  Code,
  Database,
  FileText,
  Mail,
  MessageSquare,
  Calendar,
  Clock,
  Settings,
  Play,
  Layers,
  Box,
  Bot,
  Brain,
  Wrench,
  Webhook,
  type LucideIcon,
} from 'lucide-react';

const iconMap: Record<string, LucideIcon> = {
  globe: Globe,
  'git-branch': GitBranch,
  code: Code,
  database: Database,
  'file-text': FileText,
  mail: Mail,
  'message-square': MessageSquare,
  calendar: Calendar,
  clock: Clock,
  settings: Settings,
  play: Play,
  layers: Layers,
  box: Box,
  bot: Bot,
  brain: Brain,
  wrench: Wrench,
  webhook: Webhook,
  shuffle: GitBranch,
};

interface NodeIconProps {
  icon: string;
  size?: number;
  className?: string;
  /** 图标颜色（stroke），通常传入节点分类色 */
  color?: string;
}

export function NodeIcon({ icon, size = 16, className, color }: NodeIconProps) {
  const Icon = iconMap[icon.toLowerCase()] ?? Box;
  return <Icon size={size} className={className} color={color} />;
}