const { execSync } = require('child_process');
const fs = require('fs-extra');
const path = require('path');
const packageJson = require('../package.json');

// メイン処理を非同期関数として定義
async function deploy() {
  // 設定値の取得
  const deployBasePath = process.env.DEPLOY_PATH || packageJson.config.deployPath;
  const deployPath = path.resolve(deployBasePath);

  console.log('🔨 Building SharpTools MCP Server (Release)...');
  console.log(`📁 Deploy target: ${deployPath}`);

  // Claude Desktop プロセスを終了
  const killToolPath = 'C:\\Users\\info\\source\\repos\\Experimental2025\\KillAllForClaudeDesktop\\bin\\Release\\net9.0-windows\\KillAllForClaudeDesktop.exe';
  if (fs.existsSync(killToolPath)) {
    console.log('🛑 Stopping Claude Desktop processes...');
    try {
      execSync(`"${killToolPath}"`, { stdio: 'inherit' });
      console.log('✅ Claude Desktop processes stopped.');
      // プロセス終了後に少し待機
      console.log('⏳ Waiting for file locks to release...');
      await new Promise(resolve => setTimeout(resolve, 2000));
    } catch (error) {
      console.log('⚠️ Warning: Failed to stop Claude Desktop processes:', error.message);
      console.log('📝 You may need to manually close Claude Desktop if deployment fails.');
    }
  } else {
    console.log('⚠️ KillAllForClaudeDesktop.exe not found, skipping process termination.');
  }

  try {
  // デプロイディレクトリの準備
  console.log('📂 Preparing deploy directory...');
  if (fs.existsSync(deployPath)) {
    fs.removeSync(deployPath);
  }
  fs.ensureDirSync(deployPath);

  // リリースビルド実行
  console.log('⚙️ Building Stdio Server...');
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
    console.log('✅ SharpTools MCP Server deployed successfully!');
    console.log(`📍 Location: ${deployPath}`);
    console.log(`🚀 Run: npm start`);
    console.log(`🚀 Or:  "${exePath}" --log-level ${packageJson.config.logLevel}`);
  } else {
    throw new Error('Deploy failed: Executable not found');
  }

  // ファイル一覧表示（オプション）
  if (process.argv.includes('--verbose')) {
    console.log('\n📋 Deployed files:');
    const files = fs.readdirSync(deployPath);
    files.forEach(file => {
      const stat = fs.statSync(path.join(deployPath, file));
      const size = (stat.size / 1024).toFixed(1);
      console.log(`  ${file} (${size} KB)`);
    });
  }

  } catch (error) {
    console.error('❌ Deploy failed:', error.message);
    process.exit(1);
  }
}

// 非同期関数を実行
deploy().catch(error => {
  console.error('❌ Deploy script failed:', error.message);
  process.exit(1);
});