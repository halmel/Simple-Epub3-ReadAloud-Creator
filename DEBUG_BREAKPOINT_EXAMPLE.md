# Debug Breakpoint Usage Guide

## Overview
The `AlignmentProcessor` now includes configurable debug breakpoints that allow you to pause execution when specific fragments or words are being processed. This is useful for debugging suspicious segments identified from the logger output.

## Available Breakpoint Options

### 1. Single Fragment Index Breakpoint
Pause when a specific fragment index is being processed:
```csharp
processor.DebugBreakOnFragmentIndex = 42; // Break when processing fragment 42
```

### 2. Single Word Index Breakpoint
Pause when a specific word index is being processed:
```csharp
processor.DebugBreakOnWordIndex = 100; // Break when processing word 100
```

### 3. Multiple Fragment Indices Breakpoint
Pause when any of several fragment indices are being processed:
```csharp
processor.DebugBreakOnFragmentIndices = new List<int> { 42, 43, 44, 100 };
```

### 4. Multiple Word Indices Breakpoint
Pause when any of several word indices are being processed:
```csharp
processor.DebugBreakOnWordIndices = new List<int> { 100, 200, 300 };
```

### 5. Fragment Range Breakpoint
Pause when processing fragments within a range:
```csharp
processor.DebugBreakOnFragmentRange = (Start: 40, End: 50); // Break for fragments 40-50
```

### 6. Word Range Breakpoint
Pause when processing words within a range:
```csharp
processor.DebugBreakOnWordRange = (Start: 100, End: 150); // Break for words 100-150
```

## Usage Example

```csharp
// Create your alignment processor
var processor = new AlignmentProcessor(ref bookSegments, ref transcriptSegments, wordPath, logPath, config);

// Set breakpoints based on suspicious segments from your logger
// Example: You noticed issues with fragments 42, 43, and 44
processor.DebugBreakOnFragmentIndices = new List<int> { 42, 43, 44 };

// Or if you want to debug a range of words
processor.DebugBreakOnWordRange = (Start: 100, End: 150);

// Run the alignment - execution will pause when breakpoints are hit
processor.RunAlignment();
```

## Workflow

1. **Run alignment normally** and check the logger output
2. **Identify suspicious segments** (fragments or words with low scores, failures, etc.)
3. **Set breakpoints** using the appropriate debug properties
4. **Run alignment again** - the debugger will break when those segments are processed
5. **Inspect variables** and step through the code to understand the issue

## Important Notes

- Breakpoints only work in **DEBUG** build configuration
- The `CheckDebugBreakpoint` method is called at the start of each fragment processing in `AlignMicroSegments`
- When a breakpoint is triggered, `System.Diagnostics.Debugger.Break()` is called, which will:
  - Attach a debugger if none is attached
  - Break into the debugger if one is already attached
- You can combine multiple breakpoint types (e.g., both fragment and word breakpoints)

## Example Debugging Session

```csharp
// After running alignment and checking logs, you see:
// "Failed to align fragment 42"
// "Low score at word 100"

// Set up breakpoints for these problematic areas
var processor = new AlignmentProcessor(ref bookSegments, ref transcriptSegments, wordPath, logPath, config);

processor.DebugBreakOnFragmentIndex = 42;  // Break on the failed fragment
processor.DebugBreakOnWordIndex = 100;     // Break on the low-scoring word

// Now when you run alignment, it will pause at these points
processor.RunAlignment();
```

## Tips

1. **Start with specific indices** - If you know the exact problematic fragment/word, use single index breakpoints
2. **Use ranges for investigation** - If you're not sure exactly where the issue is, use ranges to narrow it down
3. **Combine with logging** - Use the existing logger to identify suspicious areas, then use breakpoints to investigate
4. **Check both fragment and word indices** - Sometimes the issue is with how fragments map to words