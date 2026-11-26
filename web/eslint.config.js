import js from '@eslint/js';
import reactHooks from 'eslint-plugin-react-hooks';
import tseslint from 'typescript-eslint';

// Any warning fails CI (SPEC 0087): a rule that does not fit the project is turned off here, with its reason.
export default tseslint.config(
  { ignores: ['dist', 'reports', 'playwright-report', 'test-results'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['src/**/*.{ts,tsx}'],
    plugins: { 'react-hooks': reactHooks },
    rules: { 'react-hooks/rules-of-hooks': 'error', 'react-hooks/exhaustive-deps': 'error' },
  },
  {
    files: ['src/**/*.{ts,tsx}', 'e2e/**/*.ts'],
    rules: {
      // HTML or code built from text is where an injected script gets in.
      'no-eval': 'error',
      'no-implied-eval': 'error',
      'no-restricted-syntax': [
        'error',
        { selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']", message: 'Render text, never HTML.' },
        { selector: "MemberExpression[property.name=/^(inner|outer)HTML$/]", message: 'Render text, never HTML.' },
      ],
    },
  },
  {
    // Every text a person reads comes from the catalogue of its language (SPEC 0095). The audit and its labels are only for the
    // author, in Spanish (R7).
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/**/*.test.ts', 'src/testSetup.ts', 'src/i18n/**', 'src/showcase/text.*.ts', 'src/pages/AuditPage.tsx', 'src/audit.ts', 'src/visitLabels.ts', 'src/showcase/Root.tsx'],
    rules: {
      'no-restricted-syntax': [
        'error',
        { selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']", message: 'Render text, never HTML.' },
        { selector: "MemberExpression[property.name=/^(inner|outer)HTML$/]", message: 'Render text, never HTML.' },
        { selector: 'JSXText[value=/[A-Za-zÁ-ú]{2,}/]', message: 'A visible text comes from the catalogue (src/i18n).' },
        {
          selector: "JSXAttribute[name.name=/^(title|alt|placeholder|aria-label|label|hint)$/] > Literal",
          message: 'A visible text comes from the catalogue (src/i18n).',
        },
        { selector: 'Literal[value=/[áéíóúñÁÉÍÓÚÑ¿¡«»]/]', message: 'A visible text comes from the catalogue (src/i18n).' },
        { selector: 'TemplateElement[value.raw=/[áéíóúñÁÉÍÓÚÑ¿¡«»]/]', message: 'A visible text comes from the catalogue (src/i18n).' },
      ],
    },
  },
);
