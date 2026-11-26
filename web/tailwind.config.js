/** SPEC 0057 y 0058: Tailwind expone los tokens de src/index.css (única fuente). Ver docs/design-system.md. */
const v = name => `rgb(var(--${name}) / <alpha-value>)`;
export default {
  content: { relative: true, files: ['./index.html', './src/**/*.{ts,tsx}'] },
  theme: {
    extend: {
      // SPEC 0060: un escalón más para monitores anchos (≥ 1920 px). 2xl (1536) ya viene de serie.
      screens: { '3xl': '1920px' },
      colors: {
        // Marca y semántica
        navy: v('navy'), 'navy-soft': v('navy-soft'), 'navy-tint': v('navy-tint'), 'navy-2': v('navy-2'), 'on-navy': v('on-navy'), 'on-navy-2': v('on-navy-2'),
        'blue-bright': v('blue-bright'), 'success-bright': v('success-bright'), 'warning-bright': v('warning-bright'), 'critical-bright': v('critical-bright'),
        blue: v('blue'), 'blue-ink': v('blue-ink'), 'blue-soft': v('blue-soft'),
        success: v('success'), 'success-ink': v('success-ink'), 'success-soft': v('success-soft'),
        warning: v('warning'), 'warning-ink': v('warning-ink'), 'warning-soft': v('warning-soft'),
        critical: v('critical'), 'critical-ink': v('critical-ink'), 'critical-strong': v('critical-strong'), 'critical-soft': v('critical-soft'),
        // Superficies y texto
        bg: v('bg'), surface: v('surface'), sunken: v('sunken'), line: v('line'),
        ink: v('ink'), 'ink-2': v('ink-2'), 'ink-3': v('ink-3'),
        // Neutros y barra lateral
        muted: v('muted'), 'side-fg': v('side-fg'), 'side-muted': v('side-muted'),
      },
      // SPEC 0058: seis tamaños y mucho contraste (nunca < 12 px): cifra 36 · página 28 · módulo 16 · datos 14 · secundario 13 · mínimo 12.
      fontSize: {
        display: ['36px', { lineHeight: '40px', letterSpacing: '-0.025em' }],
        page: ['28px', { lineHeight: '34px', letterSpacing: '-0.02em' }],
        module: ['16px', { lineHeight: '24px', letterSpacing: '-0.005em' }],
        data: ['14px', { lineHeight: '20px' }],
        meta: ['13px', { lineHeight: '18px' }],
      },
      borderRadius: { sm: 'var(--radius-sm)', md: 'var(--radius-md)', lg: 'var(--radius-lg)', xl: 'var(--radius-xl)' },
      boxShadow: { 1: 'var(--shadow-1)', 2: 'var(--shadow-2)', navy: 'var(--shadow-navy)', pop: 'var(--shadow-pop)' },
      fontFamily: { sans: ['Inter Variable', 'system-ui', 'Segoe UI', 'sans-serif'], mono: ['JetBrains Mono Variable', 'ui-monospace', 'Consolas', 'monospace'] },
    },
  },
  plugins: [],
};
