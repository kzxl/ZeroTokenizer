# 🔤 ZeroTokenizer: Sovereign Pure C# BPE & Tiktoken Engine

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET Multi-Targeting](https://img.shields.io/badge/.NET-8.0%20%7C%204.6.2%20%7C%20Standard%202.0-purple.svg)](https://dotnet.microsoft.com/)
[![Zero External Dependencies](https://img.shields.io/badge/Dependencies-0%20(Pure%20C%23)-brightgreen.svg)]()

**ZeroTokenizer** is an ultra-fast, zero-allocation Byte-Pair Encoding (BPE), WordPiece, and Tiktoken tokenizer engine engineered in 100% pure C# for .NET. It resides in **Tier 3 (Perception & Intelligence)** of the [ZeroPlatform](https://github.com/kzxl/ZeroPlatform) ecosystem.

---

## ⚡ Key Capabilities

- **Zero-Allocation Tokenizer Pipeline**:
  - Direct span-based encoding: `int Encode(ReadOnlySpan<char> text, Span<int> destinationTokenIds)`.
  - Fast token counter: `int CountTokens(ReadOnlySpan<char> text)` without allocating integer arrays or string chunks.
  - Streaming decoding: `int Decode(ReadOnlySpan<int> tokenIds, Span<char> destinationText)`.
- **BPE & Byte-Level Fallback**:
  - Implements the standard Byte-Pair Encoding algorithm with UTF-8 byte fallback (compatible with GPT-2, GPT-4, LLaMA-3, and Qwen models).
- **Tiktoken Engine**:
  - Pre-configured regex patterns for `cl100k_base` (OpenAI GPT-4 / ChatGPT) and `o200k_base` (GPT-4o).
- **Context Token Budgeter (`TokenBudgeter`)**:
  - Greedy Knapsack packing algorithm for LLM prompts, ensuring conversation histories, system instructions, and RAG knowledge fragments never overflow the context window ($128\text{k}$ tokens).
- **Zero External Dependencies & Multi-Targeting**:
  - Compatible with `.NET 8.0+`, `.NET Framework 4.6.2+`, and `.NET Standard 2.0`.

---

## 🚀 Quick Start

### 1. Count Tokens Without Memory Allocations

```csharp
using ZeroTokenizer.Core;

var tokenizer = TiktokenTokenizer.CreateCl100kBase();

ReadOnlySpan<char> prompt = "System: Analyze factory vibration telemetry at node 4.";
int tokenCount = tokenizer.CountTokens(prompt);

Console.WriteLine($"Token count: {tokenCount}");
```

### 2. Encode and Decode Tokens

```csharp
using ZeroTokenizer.Core;

var tokenizer = TiktokenTokenizer.CreateCl100kBase();

// Encode into pre-allocated buffer
Span<int> tokenBuffer = stackalloc int[128];
int written = tokenizer.Encode("Hello, ZeroPlatform AI Agent!", tokenBuffer);

// Decode back to string
string reconstructed = tokenizer.Decode(tokenBuffer.Slice(0, written));
```

### 3. Context Token Budgeting (Knapsack Packing)

```csharp
using ZeroTokenizer.Core.Budget;

var budgeter = new TokenBudgeter(tokenizer, maxContextTokens: 4096);

budgeter.AddSystemPrompt("You are an autonomous SCADA diagnostic agent.");
budgeter.AddUserMessage("Why did Motor #3 trigger an over-current fault?");
budgeter.AddKnowledgeItem("Motor #3 Bearing Specification Manual: Max rated current is 45A.", priority: 90);

string packedPrompt = budgeter.BuildPackedPrompt();
```

---

## 📄 License

Architected and developed by **Phong Võ** (`kzxl`). Released under the **MIT License**.
