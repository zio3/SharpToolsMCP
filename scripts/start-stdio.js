const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const packageJson = require('../package.json');

const deployBasePath = process.env.DEPLOY_PATH || packageJson.config.deployPath;
const deployPath = path.resolve(deployBasePath);
const exePath = path.join(deployPath, 'SharpTools.StdioServer.exe');

// コマンドライン引数の処理
const args = process.argv.slice(2);
const logLevel = args.find(arg => arg.startsWith('--log-level='))?.split('=')[1] || packageJson.config.logLevel;
const logDir = args.find(arg => arg.startsWith('--log-directory='))?.split('=')[1] || './logs';

console.log('🚀 Starting SharpTools MCP Server...');
console.log(`📍 Location: ${deployPath}`);
console.log(`📝 Log Level: ${logLevel}`);
console.log(`📁 Log Directory: ${logDir}`);

// 実行ファイルの存在チェック
if (!fs.existsSync(exePath)) {
  console.error('❌ SharpTools MCP Server executable not found!');
  console.error(`Expected location: ${exePath}`);
  console.error('💡 Run "npm run deploy" first to build and deploy.');
  process.exit(1);
}

try {
  // MCPサーバーの起動
  const serverArgs = [
    '--log-level', logLevel,
    '--log-directory', logDir
  ];

  console.log(`⚙️ Executing: ${exePath} ${serverArgs.join(' ')}`);
  console.log('📡 Server starting... (Press Ctrl+C to stop)\n');

  const serverProcess = spawn(exePath, serverArgs, {
    stdio: 'inherit',
    cwd: deployPath
  });

  // プロセス終了ハンドリング
  serverProcess.on('error', (error) => {
    console.error('❌ Failed to start server:', error.message);
    process.exit(1);
  });

  serverProcess.on('exit', (code) => {
    if (code === 0) {
      console.log('\n✅ Server stopped gracefully.');
    } else {
      console.log(`\n⚠️ Server exited with code: ${code}`);
    }
    process.exit(code || 0);
  });

  // Ctrl+C ハンドリング
  process.on('SIGINT', () => {
    console.log('\n🛑 Stopping server...');
    serverProcess.kill('SIGTERM');
  });

} catch (error) {
  console.error('❌ Failed to start SharpTools MCP Server:', error.message);
  process.exit(1);
}