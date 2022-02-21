//@ts-check
'use strict';

const path = require('path');
const webpack = require('webpack');
/**
 * @param {string} file
 */
function _(file) {
  return path.normalize(path.resolve(__dirname, file));
}


// https://stackoverflow.com/questions/43140501/can-webpack-report-which-file-triggered-a-compilation-in-watch-mode
class WatchRunPlugin {
  apply(compiler) {

    compiler.hooks.watchRun.tap('WatchRun', (/** @type {import('webpack').Compiler} */ comp) => {

      if (comp.modifiedFiles && comp.modifiedFiles.size > 0) {

        const changedFiles = Array.from(comp.modifiedFiles)
          .map(x => {
            while (x.charAt(x.length - 1) === path.sep) {
              x = x.substring(0, x.length - 1);
            }
            return x;
          })
          .filter((x, idx, self) => self.indexOf(x) === idx);
        const changedFilesStr = changedFiles.map(file => `\n  ${file}`).join('');
        console.log('\x1b[35m-----------------------');
        console.log('CHANGED:', changedFilesStr);
        console.log('-----------------------\x1b[0m');
      }
      if (comp.removedFiles && comp.removedFiles.size > 0) {
        const removedFilesStr = Array.from(comp.removedFiles, (file) => `\n  ${file}`).join('');
        console.log('\x1b[35m-----------------------');
        console.log('REMOVED:', removedFilesStr);
        console.log('-----------------------\x1b[0m');
      }
    });
  }
}


const MiniCssExtractPlugin = require('mini-css-extract-plugin');

const outputPath = _('./../dotnet/UI.Blazor.Host/wwwroot/dist');

module.exports = (env, args) => {

  const isDevelopment = args.mode === 'development';

  /** Use this options to control /// #ifdef preprocessor */
  const ifdef = {
    DEBUG: isDevelopment,
    MEM_LEAK_DETECTION: isDevelopment && false,
    // TODO: define client js app version with NBGV (?)
    version: 1.0,
    "ifdef-verbose": false,
    "ifdef-triple-slash": true,
    "ifdef-fill-with-blanks": true,
    "ifdef-uncomment-prefix": "/// #code "
  };

  /**@type {import('webpack').Configuration}*/
  const config = {
    performance: {
      hints: false,
    },
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
        outputPath,
      ],
      followSymlinks: false,
    },
    resolve: {
      extensions: ['.ts', '.js', '...'],
      modules: [_('./node_modules'), _('./src/')],
      roots: [],
      fallback: {
        "path": false,
        "fs": false,
        "stream": require.resolve('readable-stream'),
      }
    },
    // to enable ts debug uncomment the line below
    devtool: isDevelopment ? 'source-map' : false,
    // another type of inlined source maps
    //devtool: isDevelopment ? 'eval' : false,
    plugins: [
      // @ts-ignore
      new MiniCssExtractPlugin({
        filename: '[name].css',
        ignoreOrder: true,
        experimentalUseImportModule: true,
      }),
      new WatchRunPlugin(),
      new webpack.ProvidePlugin({
        Buffer: ['buffer', 'Buffer'],
      }),
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
            },
            {
              loader: "ifdef-loader",
              options: ifdef,
            }
          ],
        },
        {
          test: /^\.d\.ts$/i,
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
          test: /\.map$/i,
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
        },
      ],
    },
    entry: {
      warmUpWorklet: {
        import: './src/worklets/warm-up-worklet-processor.ts',
        chunkLoading: false,
        asyncChunks: false,
        runtime: false,
        library: {
          type: 'module',
        }
      },
      feederWorklet: {
        import: './../dotnet/Audio.UI.Blazor/Components/AudioPlayer/worklets/feeder-audio-worklet-processor.ts',
        chunkLoading: false,
        asyncChunks: false,
        runtime: false,
        library: {
          type: 'module',
        }
      },
      opusDecoderWorker: {
        import: './../dotnet/Audio.UI.Blazor/Components/AudioPlayer/workers/opus-decoder-worker.ts',
        chunkLoading: 'import',
        asyncChunks: true,
        library: {
          type: 'module',
        }
      },
      vadWorklet: {
        import: './../dotnet/Audio.UI.Blazor/Components/AudioRecorder/worklets/audio-vad-worklet-processor.ts',
        chunkLoading: false,
        asyncChunks: false,
        runtime: false,
        library: {
          type: 'module',
        }
      },
      vadWorker: {
        import: './../dotnet/Audio.UI.Blazor/Components/AudioRecorder/workers/audio-vad-worker.ts',
        chunkLoading: 'import',
        asyncChunks: true,
        library: {
          type: 'module',
        }
      },
      opusEncoderWorklet: {
        import: './../dotnet/Audio.UI.Blazor/Components/AudioRecorder/worklets/opus-encoder-worklet-processor.ts',
        chunkLoading: false,
        asyncChunks: false,
        runtime: false,
        library: {
          type: 'module',
        }
      },
      opusEncoderWorker: {
        import: './../dotnet/Audio.UI.Blazor/Components/AudioRecorder/workers/opus-encoder-worker.ts',
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
          type: 'this',
        },
      }
    },
    output: {
      clean: true,
      path: outputPath,
      globalObject: 'globalThis',
      filename: '[name].js',
      publicPath: "/dist/",
    },
    experiments: {
      /* https://github.com/webpack/webpack/issues/11382 */
      outputModule: true,
    },
  };
  return config;
};
