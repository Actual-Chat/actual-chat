//@ts-check
'use strict';

// https://stackoverflow.com/questions/43140501/can-webpack-report-which-file-triggered-a-compilation-in-watch-mode
class WatchRunPlugin {
  apply(compiler) {
    compiler.hooks.watchRun.tap('WatchRun', (/** @type {import('webpack').Compiler} */ comp) => {
      if (comp.modifiedFiles && comp.modifiedFiles.size > 0) {
        const changedFiles = Array.from(comp.modifiedFiles, (file) => `\n  ${file}`).join('');
        console.log('\x1b[35m-----------------------');
        console.log('FILES CHANGED:', changedFiles);
        console.log('-----------------------\x1b[0m');
      }
      if (comp.removedFiles && comp.removedFiles.size > 0) {
        const removedFiles = Array.from(comp.removedFiles, (file) => `\n  ${file}`).join('');
        console.log('\x1b[35m-----------------------');
        console.log('FILES REMOVED:', removedFiles);
        console.log('-----------------------\x1b[0m');
      }
    });
  }
}

const path = require('path');
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
      usedExports: true,
      splitChunks: {
        cacheGroups: {
          styles: {
            name: 'styles',
            type: 'css/mini-extract',
          },
        },
      },
    },
    watchOptions: {
      aggregateTimeout: 30, // ms
      ignored: [
        _('node_modules'),
        _('../dotnet/UI.Blazor.Host/wwwroot'),
        '**/*.cs',
        _('../../tests/'),
        _('../../docs/'),
        '**/*.csproj',
        '**/*.targets',
        '**/*.props',
      ],
      followSymlinks: false,
    },
    resolve: {
      extensions: ['.ts', '.js', '...'],
      modules: [_('./node_modules')],
      fallback: {
        "path": false,
        "fs": false
      }
    },
    // to enable ts debug uncomment the line below
    devtool: args.mode === 'development' ? 'source-map' : false,
    // another type of inlined source maps
    //devtool: args.mode === 'development' ? 'eval' : false,
    plugins: [
      // @ts-ignore
      new MiniCssExtractPlugin({
        filename: '[name].css',
        ignoreOrder: true,
        experimentalUseImportModule: true,
      }),
      new WatchRunPlugin(),
    ],
    module: {
      // all files with a '.ts' or '.tsx' extension will be handled by 'ts-loader'
      rules: [
        {
          test: /\.tsx?$/i,
          exclude: /node_modules/,
          use: [
            {
              loader: 'ts-loader',
              options: {
                // disable type checking in development (vs does this anyway)
                transpileOnly: args.mode === 'development',
                experimentalWatchApi: true,
                configFile: _('./tsconfig.json')
              },
            }
          ],
        },
        {
          test: /^\.d.ts$/i,
          loader: 'ignore-loader'
        },
        {
          test: /\.css$/i,
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
                sourceMap: true,
                postcssOptions: {
                  config: _('./postcss.config.js'),
                },
              }
            }
          ]
        },
        {
          test: /\.wasm$/i,
          type: 'asset/resource',
          generator: {
            filename: 'wasm/[name][ext][query]'
          }
        },
        {
          test: /\.onnx$/i,
          type: 'asset/resource',
          generator: {
            filename: 'wasm/[name].bin'
          }
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
      vadWorklet: {
        import: './../dotnet/Audio.UI.Blazor/Components/AudioRecorder/worklets/audio-vad.worklet-module.ts',
        chunkLoading: false,
        asyncChunks: false,
        runtime: false,
        library: {
          type: 'module',
        }
      },
      vadWorker: {
        import: './../dotnet/Audio.UI.Blazor/Components/AudioRecorder/workers/audio-vad.worker.ts',
        chunkLoading: false,
        asyncChunks: false,
        runtime: false,
        library: {
          type: 'module',
        }
      },
      encoderWorker: {
        import: './../dotnet/Audio.UI.Blazor/Components/AudioRecorder/workers/encoder-worker.ts',
        chunkLoading: false,
        asyncChunks: false,
        runtime: false,
        library: {
          type: 'module',
        }
      },
      bundle: {
        import: './index.ts',
        library: {
          type: 'this'
        },
      }
    },
    output: {
      clean: true,
      path: outputPath,
      globalObject: 'globalThis',
      filename: '[name].js',
      publicPath: "/dist/"
    },
    experiments: {
      /* https://github.com/webpack/webpack/issues/11382 */
      outputModule: true,
    }
  };
  return config;
};
