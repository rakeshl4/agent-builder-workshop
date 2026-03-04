# Lab 06: PII Filtering Middleware

In this lab, you will implement **PII (Personally Identifiable Information) filtering middleware** to redact sensitive information from conversations.

You will create middleware that filters both user inputs and agent outputs to protect privacy by redacting phone numbers, email addresses, and names.

By the end of this lab, you will:

- ✅ Understand how to implement agent-level middleware
- ✅ Protect sensitive information in both directions (input and output)

## Instructions

### Step 1: Navigate to the Lab Folder

```bash
cd labs/00-foundations/lab06-middleware
```

### Step 3: Run the Program

With .NET 10's file-based apps, you can run the single .cs file directly:

```bash
dotnet run Program.cs
```

Or in Visual Studio Code, open Program.cs and click the **Run** button that appears above the code.

### Step 4: Observe the Output

The program identifies and redacts PII from the user input and agent responses.