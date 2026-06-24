import { defineConfig } from 'vitepress'

const sidebarJa = [
  {
    text: 'ガイド',
    items: [{ text: 'はじめに', link: '/guide/getting-started' }],
  },
  {
    text: '機能',
    items: [
      { text: 'Mesh Info Display', link: '/features/mesh-info-display' },
      { text: 'Toggle Objects Active', link: '/features/toggle-objects-active' },
      { text: 'Component Manager', link: '/features/component-manager' },
      { text: 'Material Copier', link: '/features/material-copier' },
      { text: 'MA Material Helper', link: '/features/ma-material-helper' },
      { text: 'AO Bounds Setter', link: '/features/ao-bounds-setter' },
      { text: 'Missing BlendShape Inserter', link: '/features/missing-blendshape-inserter' },
    ],
  },
]

const sidebarEn = [
  {
    text: 'Guide',
    items: [{ text: 'Getting Started', link: '/en/guide/getting-started' }],
  },
  {
    text: 'Features',
    items: [
      { text: 'Mesh Info Display', link: '/en/features/mesh-info-display' },
      { text: 'Toggle Objects Active', link: '/en/features/toggle-objects-active' },
      { text: 'Component Manager', link: '/en/features/component-manager' },
      { text: 'Material Copier', link: '/en/features/material-copier' },
      { text: 'MA Material Helper', link: '/en/features/ma-material-helper' },
      { text: 'AO Bounds Setter', link: '/en/features/ao-bounds-setter' },
      { text: 'Missing BlendShape Inserter', link: '/en/features/missing-blendshape-inserter' },
    ],
  },
]

const siteUrl = 'https://kxn4t.github.io/kanameliser-editor-plus'
const ogImage = `${siteUrl}/og-image.png`

export default defineConfig({
  title: 'Kanameliser Editor Plus',
  base: '/kanameliser-editor-plus/',

  head: [
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:site_name', content: 'Kanameliser Editor Plus' }],
    ['meta', { property: 'og:image', content: ogImage }],
    ['meta', { property: 'og:url', content: siteUrl }],
    ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
    ['meta', { name: 'twitter:image', content: ogImage }],
    ['meta', { name: 'twitter:site', content: '@kanameliser' }],
  ],

  locales: {
    root: {
      label: '日本語',
      lang: 'ja',
      description: 'Unity・VRChat向けエディター拡張セット',
      themeConfig: {
        nav: [
          { text: 'ドキュメント', link: '/guide/getting-started' },
          { text: '更新履歴', link: '/changelog' },
        ],
        sidebar: sidebarJa,
        outline: { label: 'このページ' },
        docFooter: { prev: '前のページ', next: '次のページ' },
        darkModeSwitchLabel: 'テーマ',
        lightModeSwitchTitle: 'ライトモードに切り替え',
        darkModeSwitchTitle: 'ダークモードに切り替え',
        sidebarMenuLabel: 'メニュー',
        returnToTopLabel: 'トップに戻る',
      },
    },
    en: {
      label: 'English',
      lang: 'en',
      description: 'A set of useful editor extensions for Unity and VRChat',
      themeConfig: {
        nav: [
          { text: 'Docs', link: '/en/guide/getting-started' },
          { text: 'Changelog', link: '/en/changelog' },
        ],
        sidebar: sidebarEn,
      },
    },
  },

  themeConfig: {
    socialLinks: [
      { icon: 'github', link: 'https://github.com/kxn4t/kanameliser-editor-plus' },
    ],
  },
})
