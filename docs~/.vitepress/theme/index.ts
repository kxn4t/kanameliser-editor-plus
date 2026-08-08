import { h } from 'vue'
import DefaultTheme from 'vitepress/theme'
import BetaBanner from './BetaBanner.vue'
import './custom.css'

export default {
  extends: DefaultTheme,
  Layout: () =>
    h(DefaultTheme.Layout, null, {
      'layout-top': () => h(BetaBanner),
    }),
}
