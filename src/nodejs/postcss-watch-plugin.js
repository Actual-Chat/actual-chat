// @ts-check
"use strict";
const path = require('path');
const fs = require('fs');
/**
 * @param {string} file
 */
function _(file) {
  return path.normalize(path.resolve(__dirname, file));
}

const dirs = fs.readdirSync(_('./../dotnet/'), { withFileTypes: true })
  .filter(d => d.isDirectory() && d.name.indexOf("UI.Blazor") >= 0)
  .map(d => _(`./../dotnet/${d.name}`) + path.sep)
  .concat(_(`./../dotnet/App.Server`) + path.sep);

module.exports = (opts = {}) => {
  return {
    postcssPlugin: 'postcss-watch-plugin',
    Once(root, { result }) {
      for (let i = 0; i < dirs.length; ++i) {
        result.messages.push({
          plugin: 'postcss-watch-plugin',
          type: 'dir-dependency',
          dir: dirs[i],
          glob: '**/*.razor',
        });
      }
    }
  };
};

module.exports.postcss = true;
