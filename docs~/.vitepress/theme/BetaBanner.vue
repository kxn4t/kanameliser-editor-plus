<script setup lang="ts">
import { useData } from 'vitepress'

const { theme, lang } = useData()
</script>

<template>
  <div v-if="theme.versionBadge?.channel === 'beta'" class="beta-banner">
    <span>
      <template v-if="lang.startsWith('ja')">
        これはベータ版のドキュメントです。
        <a v-if="theme.versionBadge.stableUrl" :href="theme.versionBadge.stableUrl"
          >安定版のドキュメントを見る</a
        >
      </template>
      <template v-else>
        This documentation is for a beta release.
        <a v-if="theme.versionBadge.stableUrl" :href="theme.versionBadge.stableUrl"
          >View the stable documentation</a
        >
      </template>
    </span>
  </div>
</template>
