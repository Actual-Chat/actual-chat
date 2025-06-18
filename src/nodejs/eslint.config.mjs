// import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';
import tsParser from '@typescript-eslint/parser';
import prettier from 'eslint-plugin-prettier'

function tsOnly(cfg) {
    return cfg.map((config) => ({
        ...config,
        files: ['**/*.ts'], // We use TS config only for TS files
    }));
}

export default tseslint.config(
    ...tsOnly(tseslint.configs.recommendedTypeChecked),
    ...tsOnly(tseslint.configs.strictTypeChecked),
    ...tsOnly(tseslint.configs.stylisticTypeChecked),
    {
        files: ['**/*.ts'],
        rules: {
            indent: ['error', 4],
            quotes: ['error', 'single', {
                allowTemplateLiterals: true,
                avoidEscape: true,
            }],
            "@typescript-eslint/no-extraneous-class": ['error', {
                allowStaticOnly: true,
            }],
            "@typescript-eslint/restrict-template-expressions": ['error', {
                allowNumber: true,
                allowBoolean: true,
                allowNullish: true,
            }],
            "@typescript-eslint/no-non-null-assertion": 'off',
            "@typescript-eslint/no-confusing-void-expression": ["error", {
                "ignoreArrowShorthand": true,
            }],
        },
        languageOptions: {
            parser: tsParser,
            ecmaVersion: 2020,
            sourceType: 'module',
            parserOptions: {
                tsconfigRootDir: import.meta.dirname,
                projectService: true,
                allowDefaultProject: ['*.ts']
            },
        },
        plugins: {
            prettier,
        },
    },
);
