import type { SidebarConfig } from '@vuepress/theme-default'

export const sidebarZh: SidebarConfig = {
  '/zh/start/': [
    {
      text: 'NCB 概要',
      children: [
        '/zh/start/home/index.md',
      ],
    },
  ]
}
