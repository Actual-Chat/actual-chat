// import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';
import tsParser from '@typescript-eslint/parser';
import prettier from 'eslint-plugin-prettier'

const tsFiles = ['src/nodejs/**/*.ts', 'src/dotnet/**/*.ts'];

function tsOnly(cfg) {
    return cfg.map((config) => ({
        ...config,
        files: tsFiles,
    }));
}

export default tseslint.config(
    {
        ignores: ['**/dist/', '**/node_modules/', '**/obj/', '**/bin/', '**/wwwroot/'],
    },
    ...tsOnly(tseslint.configs.recommendedTypeChecked),
    ...tsOnly(tseslint.configs.strictTypeChecked),
    ...tsOnly(tseslint.configs.stylisticTypeChecked),
    {
        files: tsFiles,
        rules: {
            indent: ['error', 4],
            quotes: ['error', 'single', {
                allowTemplateLiterals: true,
                avoidEscape: true,
            }],
            'object-curly-spacing': ['error', 'always'],
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
            '@typescript-eslint/dot-notation': 'off',
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
            '@typescript-eslint': tseslint.plugin,
            prettier,
        },
    },
);
