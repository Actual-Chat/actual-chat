//@ts-check

'use strict';

const path = require('path');
//const fs = require("fs");
// entry: () => fs.readdirSync("./React/").filter(f => f.endsWith(".js")).map(f => `./React/${f}`),
const MiniCssExtractPlugin = require('mini-css-extract-plugin');

/**
 * @param {string} file
 */
function _(file) {
  return path.normalize(path.resolve(__dirname, file));
}

const outputPath = _('./../dotnet/UI.Blazor.Host/wwwroot/dist');

module.exports = (env, args) => {
  /**@type {import('webpack').Configuration}*/
  const config = {
    optimization: {
      // prevent process.env.NODE_ENV overriding by --mode
      nodeEnv: false,
      // workaround of https://github.com/webpack-contrib/mini-css-extract-plugin/issues/85
      // https://github.com/webpack/webpack/issues/7300#issuecomment-702840962
      // removes '1.bundle.js' and other trash from emitting
      removeEmptyChunks: true,
      splitChunks: {
        cacheGroups: {
          styles: {
            name: 'styles',
            type: 'css/mini-extract',
            //chunks: 'all',
            //enforce: true,
          },
        },
      },
    },
    watchOptions: {
      aggregateTimeout: 50, // ms
      ignored: ['node_modules/**', outputPath + '/**'],
    },
    resolve: {
      extensions: ['.ts', '.js', '...'],
      modules: [_('node_modules')]
    },
    //devtool: args.mode === 'development' ? 'eval' : false,
    devtool: args.mode === 'development' ? 'source-map' : false,
    plugins: [
      // @ts-ignore
      new MiniCssExtractPlugin({
        filename: '[name].css',
        ignoreOrder: true,
        experimentalUseImportModule: true,
      })
    ],
    module: {
      // all files with a '.ts' or '.tsx' extension will be handled by 'ts-loader'
      rules: [
        {
          test: /\.tsx?$/i,
          use: [
            {
              loader: 'ts-loader',
              options: {
                // disable type checking in development (vs does this anyway)
                transpileOnly: args.mode === 'development',
                experimentalWatchApi: true,
              },
            }
          ], exclude: /node_modules/
        },
        {
          test: /\.css$/i,
          //type: "css",
          use: [
            {
              loader: MiniCssExtractPlugin.loader,
              options: {
                esModule: false,
              }
            },
            'css-loader',
            {
              loader: 'postcss-loader',
              options: {
                sourceMap: false,
                postcssOptions: {
                  config: _('./postcss.config.js'),
                },
              }
            }
          ]
        },
        {
          test: /\.(ttf|eot|svg|woff(2)?)$/i,
          type: 'asset/resource',
          generator: {
            filename: 'fonts/[name][ext][query]'
          }
        }
      ]
    },
    entry: {
      bundle: './index.ts',
    },
    output: {
      path: outputPath,
      filename: '[name].js',
      library: {
        type: 'this'
      },
      publicPath: "/dist/"
    }
  };
  return config;
};
