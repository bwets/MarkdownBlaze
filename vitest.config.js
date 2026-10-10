import { defineConfig } from 'vitest/config';

// The modules under src/MarkdownBlaze/wwwroot/js are plain ES modules that the WebView loads as they
// are; vitest imports them the same way, over jsdom for the few that touch the DOM.
export default defineConfig({
  test: {
    environment: 'jsdom',
    include: ['tests/js/**/*.test.js'],
  },
});
