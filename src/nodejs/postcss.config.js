module.exports = (api) => {
  // `api.file` - path to the file
  // `api.mode` - `mode` value of webpack, please read https://webpack.js.org/configuration/mode/
  // `api.webpackLoaderContext` - loader context for complex use cases
  // `api.env` - alias `api.mode` for compatibility with `postcss-cli`
  // `api.options` - the `postcssOptions` options
  const tailwindcssConfig = require('./tailwind.config.js')({ env: api.mode });

  return {
    plugins: [
      require('./postcss-watch-plugin.js'),
      require('tailwindcss')(tailwindcssConfig),
      require('autoprefixer'),
      ...(api.mode === 'production' ? [require('cssnano')({ preset: 'default' })] : [])
    ]
  };
};
