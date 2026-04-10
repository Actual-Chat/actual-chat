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
        ignores: ['**/dist/', '**/node_modules/', '**/obj/', '**/bin/', '**/wwwroot/', 'docs/.vitepress/cache/'],
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
    {
        // Forked from /proj/ActualLab.Fusion/ts/packages/{core,rpc}/src/ (commit 42c5e4013).
        // Keep these files close to upstream style so future merges stay cheap:
        //   - upstream uses 2-space indent, double quotes
        //   - upstream uses tseslint.configs.strict only, not strictTypeChecked/stylisticTypeChecked
        // Do not auto-format these files to ActualChat style.
        files: [
            'src/nodejs/src/actuallab-core/**/*.ts',
            'src/nodejs/src/actuallab-rpc/**/*.ts',
        ],
        rules: {
            indent: ['error', 2, { SwitchCase: 1 }],
            quotes: ['error', 'double', {
                allowTemplateLiterals: true,
                avoidEscape: true,
            }],
            '@typescript-eslint/no-explicit-any': 'off',
            '@typescript-eslint/no-unnecessary-condition': 'off',
            '@typescript-eslint/no-unnecessary-type-assertion': 'off',
            '@typescript-eslint/no-unsafe-assignment': 'off',
            '@typescript-eslint/no-unsafe-member-access': 'off',
            '@typescript-eslint/no-unsafe-call': 'off',
            '@typescript-eslint/no-unsafe-argument': 'off',
            '@typescript-eslint/no-unsafe-return': 'off',
            '@typescript-eslint/no-unsafe-function-type': 'off',
            '@typescript-eslint/no-unnecessary-type-parameters': 'off',
            '@typescript-eslint/no-this-alias': 'off',
            '@typescript-eslint/array-type': 'off',
            '@typescript-eslint/require-await': 'off',
            '@typescript-eslint/no-empty-function': 'off',
            '@typescript-eslint/consistent-generic-constructors': 'off',
            '@typescript-eslint/class-literal-property-style': 'off',
        },
    },
);
