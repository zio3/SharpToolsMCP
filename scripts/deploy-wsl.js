const { execSync } = require('child_process');
const fs = require('fs-extra');
const path = require('path');
const packageJson = require('../package.json');

// メイン処理を非同期関数として定義
async function deployWsl() {
  // 設定値の取得
  const deployBasePath = process.env.DEPLOY_PATH_WSL || packageJson.config.deployPathWsl;
  const deployPath = path.resolve(deployBasePath);

  console.log('🔨 Building SharpTools MCP Server for WSL (Release)...');
  console.log(`📁 Deploy target: ${deployPath}`);

  try {
    // デプロイディレクトリの準備
    console.log('📂 Preparing WSL deploy directory...');
    if (fs.existsSync(deployPath)) {
      fs.removeSync(deployPath);
    }
    fs.ensureDirSync(deployPath);

    // WSL用リリースビルド実行
    console.log('⚙️ Building Stdio Server for WSL...');
    execSync(
      `dotnet publish SharpTools.StdioServer/SharpTools.StdioServer.csproj ` +
      `-c Release ` +
      `-o "${deployPath}" ` +
      `--no-restore ` +
      `--verbosity quiet`,
      { 
        stdio: 'inherit',
        cwd: process.cwd()
      }
    );

    // デプロイ後の確認
    const exePath = path.join(deployPath, 'SharpTools.StdioServer.exe');
    if (fs.existsSync(exePath)) {
      console.log('✅ SharpTools MCP Server for WSL deployed successfully!');
      console.log(`📍 Location: ${deployPath}`);
      console.log(`🐧 WSL Usage: dotnet "${exePath}" --log-level ${packageJson.config.logLevel}`);
      
      // WSL用パスの表示
      const wslPath = deployPath.replace(/^C:/, '/mnt/c').replace(/\\/g, '/');
      console.log(`🐧 WSL Path: ${wslPath}/SharpTools.StdioServer.exe`);
    } else {
      throw new Error('Deploy failed: Executable not found');
    }

    // ファイル一覧表示（オプション）
    if (process.argv.includes('--verbose')) {
      console.log('\n📋 Deployed files for WSL:');
      const files = fs.readdirSync(deployPath);
      files.forEach(file => {
        const stat = fs.statSync(path.join(deployPath, file));
        const size = (stat.size / 1024).toFixed(1);
        console.log(`  ${file} (${size} KB)`);
      });
    }

  } catch (error) {
    console.error('❌ WSL Deploy failed:', error.message);
    process.exit(1);
  }
}

// 非同期関数を実行
deployWsl().catch(error => {
  console.error('❌ WSL Deploy script failed:', error.message);
  process.exit(1);
});