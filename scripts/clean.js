const fs = require('fs-extra');
const path = require('path');
const packageJson = require('../package.json');

const deployBasePath = process.env.DEPLOY_PATH || packageJson.config.deployPath;
const deployBasePathWsl = process.env.DEPLOY_PATH_WSL || packageJson.config.deployPathWsl;

console.log('🧹 Cleaning SharpTools deploy outputs...');

try {
  let cleanedAny = false;

  // Windows版デプロイディレクトリのクリーンアップ
  if (fs.existsSync(deployBasePath)) {
    console.log(`📂 Removing Windows deploy directory: ${deployBasePath}`);
    fs.removeSync(deployBasePath);
    cleanedAny = true;
  }

  // WSL版デプロイディレクトリのクリーンアップ
  if (fs.existsSync(deployBasePathWsl)) {
    console.log(`📂 Removing WSL deploy directory: ${deployBasePathWsl}`);
    fs.removeSync(deployBasePathWsl);
    cleanedAny = true;
  }

  if (cleanedAny) {
    console.log('✅ Deploy directories cleaned successfully!');
  } else {
    console.log('📂 No deploy directories found, nothing to clean.');
  }

} catch (error) {
  console.error('❌ Cleanup failed:', error.message);
  process.exit(1);
}