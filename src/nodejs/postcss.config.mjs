export default {
    plugins: {
        './postcss-watch-plugin.js': {},
        'postcss-import': {},
        'tailwindcss': {},
        'autoprefixer': {
            overrideBrowserslist: ['last 2 versions', '>0.2%'],
        },
        // '@tailwindcss/postcss': {},
        ...(process.env.NODE_ENV === 'production' ? {
            'cssnano': {
                preset: 'default',
            }
        } : {}),
    },
};
