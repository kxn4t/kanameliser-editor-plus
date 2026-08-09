import { defineConfig, type HeadConfig } from 'vitepress'
import type { DefaultTheme } from 'vitepress'

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
      { text: 'FBX Settings Copier', link: '/features/fbx-settings-copier' },
      { text: 'MA Material Helper', link: '/features/ma-material-helper' },
      { text: 'Merge Skinned Mesh (Color Menu Safe)', link: '/features/color-menu-safe-merge' },
      { text: 'AO Bounds Setter', link: '/features/ao-bounds-setter' },
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
      { text: 'FBX Settings Copier', link: '/en/features/fbx-settings-copier' },
      { text: 'MA Material Helper', link: '/en/features/ma-material-helper' },
      { text: 'Merge Skinned Mesh (Color Menu Safe)', link: '/en/features/color-menu-safe-merge' },
      { text: 'AO Bounds Setter', link: '/en/features/ao-bounds-setter' },
    ],
  },
]

const siteTitle = 'Kanameliser Editor Plus'
const siteUrl = 'https://kxn4t.github.io/kanameliser-editor-plus'
const ogImage = `${siteUrl}/og-image.png`
const descriptionJa = 'Unity・VRChat向けエディター拡張セット'
const descriptionEn = 'A set of useful editor extensions for Unity and VRChat'

// Version info injected by the docs deployment workflow (.github/workflows/docs.yml).
// All variables are unset for local dev builds.
const docsVersion = process.env.DOCS_VERSION
const docsChannel = process.env.DOCS_CHANNEL
const stableVersion = process.env.DOCS_STABLE_VERSION
const betaVersion = process.env.DOCS_BETA_VERSION

const versionMenuItems: DefaultTheme.NavItemWithLink[] = [
  ...(stableVersion ? [{ text: `v${stableVersion} (stable)`, link: `${siteUrl}/` }] : []),
  ...(betaVersion ? [{ text: `v${betaVersion} (beta)`, link: `${siteUrl}/beta/` }] : []),
]

const versionNav: DefaultTheme.NavItem[] = docsVersion
  ? [
      versionMenuItems.length > 0
        ? { text: `v${docsVersion}`, items: versionMenuItems }
        : { text: `v${docsVersion}`, link: `${siteUrl}/` },
    ]
  : []

const head: HeadConfig[] = [
  ['meta', { property: 'og:type', content: 'website' }],
  ['meta', { property: 'og:site_name', content: siteTitle }],
  ['meta', { property: 'og:image', content: ogImage }],
  ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
  ['meta', { name: 'twitter:image', content: ogImage }],
  ['meta', { name: 'twitter:site', content: '@kanameliser' }],
]

// Per-page OG tags. X (Twitter) requires og:title/twitter:title to render a
// card at all — it does not fall back to the <title> tag — and VitePress only
// emits <title>/description on its own, so inject them per page here.
const pageUrlBase = docsChannel === 'beta' ? `${siteUrl}/beta` : siteUrl

if (docsChannel === 'beta') {
  // Keep beta docs out of search results
  head.push(['meta', { name: 'robots', content: 'noindex' }])
  // Reserve space for the fixed beta banner (BetaBanner.vue); the default
  // theme offsets nav/sidebar/content by --vp-layout-top-height
  head.push([
    'style',
    {},
    ':root { --vp-layout-top-height: 32px; } @media (max-width: 560px) { :root { --vp-layout-top-height: 52px; } }',
  ])
}

export default defineConfig({
  title: siteTitle,
  base: '/kanameliser-editor-plus/',
  // GitHub Pages serves /foo from foo.html natively, so no host config is needed
  cleanUrls: true,

  head,

  transformPageData(pageData) {
    const title = pageData.title ? `${pageData.title} | ${siteTitle}` : siteTitle
    const description =
      pageData.description ||
      (pageData.relativePath.startsWith('en/') ? descriptionEn : descriptionJa)
    const pagePath = pageData.relativePath
      .replace(/(^|\/)index\.md$/, '$1')
      .replace(/\.md$/, '')
    const pageHead = (pageData.frontmatter.head ??= [])
    pageHead.push(
      ['meta', { property: 'og:title', content: title }],
      ['meta', { property: 'og:description', content: description }],
      ['meta', { property: 'og:url', content: `${pageUrlBase}/${pagePath}` }],
    )
  },

  locales: {
    root: {
      label: '日本語',
      lang: 'ja',
      description: descriptionJa,
      themeConfig: {
        nav: [
          { text: 'ドキュメント', link: '/guide/getting-started' },
          { text: '更新履歴', link: '/changelog' },
          ...versionNav,
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
      description: descriptionEn,
      themeConfig: {
        nav: [
          { text: 'Docs', link: '/en/guide/getting-started' },
          { text: 'Changelog', link: '/en/changelog' },
          ...versionNav,
        ],
        sidebar: sidebarEn,
      },
    },
  },

  themeConfig: {
    socialLinks: [
      { icon: 'github', link: 'https://github.com/kxn4t/kanameliser-editor-plus' },
    ],
    // Custom key consumed by the theme (BetaBanner.vue)
    versionBadge: {
      channel: docsChannel,
      stableUrl: stableVersion ? `${siteUrl}/` : undefined,
    },
  } as DefaultTheme.Config,
})
