using System;
using System.Threading.Tasks;

namespace SharpTools.Tests.TestData
{
    /// <summary>
    /// アクセス修飾子テスト用クラス
    /// </summary>
    public class AccessModifierTestClass
    {
        /// <summary>
        /// publicメソッド
        /// </summary>
        public string PublicMethod()
        {
            return "Public";
        }

        /// <summary>
        /// privateメソッド
        /// </summary>
        private string PrivateMethod()
        {
            return "Private";
        }

        /// <summary>
        /// protectedメソッド
        /// </summary>
        protected string ProtectedMethod()
        {
            return "Protected";
        }

        /// <summary>
        /// internalメソッド
        /// </summary>
        internal string InternalMethod()
        {
            return "Internal";
        }

        /// <summary>
        /// staticメソッド
        /// </summary>
        public static string StaticMethod()
        {
            return "Static";
        }

        /// <summary>
        /// 非同期メソッド
        /// </summary>
        public async Task<string> AsyncMethod()
        {
            await Task.Delay(1);
            return "Async";
        }
    }
}
