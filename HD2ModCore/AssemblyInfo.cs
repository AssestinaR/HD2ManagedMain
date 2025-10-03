using System.Runtime.CompilerServices;

// 作用：允许测试程序集访问 internal 成员以便进行单元测试。
// Purpose: Allows the test assembly to access internal members for unit testing.
[assembly: InternalsVisibleTo("HD2ModCore.Tests")]
