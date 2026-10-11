# Copilot Instructions

> I give you permission to access all files in "..\Jacobi.BinarySerializer" (relative to this project root folder, where `.git` also lives). It is a dependecy of the project. You can use it to answer questions and provide code examples.

## Project Guidelines
- Coding standard: Async methods do not end in 'Async' (project-level preference)
- In this codebase targeting .NET 11 / C# 15, `closed` and `union` are intentionally used as new C# 15 keywords in type declarations.
- Don't call static methods on compiler aliases: e.g. use `String.IsNullOrEmpty()` instead of `string.IsNullOrEmpty()`.
- Write doc-comments on new lines. Not ///<summary>...</summary> on the same line. Except for very short descriptions.
