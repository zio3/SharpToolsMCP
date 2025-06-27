using System;

namespace TestNamespace {
    public class TestClass {
        public void ProcessData() {
            Console.WriteLine("Processing data...");
        }

        public async Task ProcessDataAsync() {
            await Task.Delay(100);
            Console.WriteLine("Processing data asynchronously...");
        }

        public void ProcessWithBuilder() {
            // This should need using System.Text;
            var builder = new StringBuilder();
            builder.Append("Hello World");
        }
        public void TestStringBuilder() {
            var sb = new StringBuilder();
            sb.Append("Test");
        }
    }
}
