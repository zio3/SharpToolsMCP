using System;

namespace TestProject {
    public class TestClass {
        public string Process(string input) {
            return $"String with Full Qualification: {input}";
        }
        public string Process(int input) {
            return $"Integer Updated: {input * 10}";
        }
        public string TestMethod(string input) {
            return $"アクセス修飾子自動継承テスト: {input}";
        }
    }
}