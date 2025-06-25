using System;
using System.Threading.Tasks;

namespace SharpTools.Tests.TestData {
    /// <summary>
    /// オーバーロードメソッドテスト用クラス
    /// </summary>
    public class OverloadTestClass {
        /// <summary>
        /// 文字列を処理するメソッド
        /// </summary>
        /// <param name="input">入力文字列</param>
        /// <returns>処理結果</returns>
        public string Process(string input) {
            return $"String: {input}";
        }
        /// <summary>
        /// 整数を処理するメソッド - MCPテスト更新版
        /// </summary>
        /// <param name="input">入力整数</param>
        /// <returns>処理結果</returns>
        public string Process(int input) {
            return $"Updated Int: {input * 10}";
        }
        /// <summary>
        /// 浮動小数点数を処理するメソッド
        /// </summary>
        /// <param name="input">入力浮動小数点数</param>
        /// <returns>処理結果</returns>
        public string Process(double input) {
            return $"Double: {input}";
        }

        /// <summary>
        /// 2つのパラメータを受け取るメソッド
        /// </summary>
        /// <param name="text">文字列</param>
        /// <param name="number">数値</param>
        /// <returns>処理結果</returns>
        public string Process(string text, int number) {
            return $"String+Int: {text}-{number}";
        }
    }
}
