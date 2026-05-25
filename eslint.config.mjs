// import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';
import tsParser from '@typescript-eslint/parser';
import prettier from 'eslint-plugin-prettier'

const tsFiles = ['src/nodejs/**/*.ts', 'src/dotnet/**/*.ts', 'tests/ts/**/*.ts'];

function tsOnly(cfg) {
    return cfg.map((config) => ({
        ...config,
        files: tsFiles,
    }));
}

export default tseslint.config(
    {
        ignores: [
            '**/.nuget/',
            '**/dist/',
            '**/node_modules/',
            '**/obj/',
            '**/bin/',
            '**/wwwroot/',
            'docs/.vitepress/cache/',
        ],
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
            '@typescript-eslint/no-unused-vars': [
                'error', // or 'warn'
                {
                    caughtErrors: 'all',            // also check catch (err) { … }
                    vars: 'all',

                    // ── The important part ──
                    argsIgnorePattern: '^_',           // arguments like _req, _,
                    varsIgnorePattern: '^_',           // let _sequenceNumber = …
                    caughtErrorsIgnorePattern: '^_',   // catch (_err) { … }
                    destructuredArrayIgnorePattern: '^_',  // const [a, _b, c] = …

                    // Optional but very commonly used together:
                    ignoreRestSiblings: true,       // const { a, ...rest } = obj;   ← rest is ignored
                },
            ],
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
