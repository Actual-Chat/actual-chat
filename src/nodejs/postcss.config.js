module.exports = (api) => {
  // `api.file` - path to the file
  // `api.mode` - `mode` value of webpack, please read https://webpack.js.org/configuration/mode/
  // `api.webpackLoaderContext` - loader context for complex use cases
  // `api.env` - alias `api.mode` for compatibility with `postcss-cli`
  // `api.options` - the `postcssOptions` options
  return {
    plugins: [
      require('./postcss-watch-plugin.js'),
      require('tailwindcss'),
      require('autoprefixer')({ overrideBrowserslist: ['last 2 versions', '>0.2%'] }),
      ...(api.mode === 'production' ? [require('cssnano')({ preset: 'default' })] : [])
    ]
  };
};
