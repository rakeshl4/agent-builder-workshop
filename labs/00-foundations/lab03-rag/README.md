# Lab 03: Travel Assistant with RAG

In this lab, you will enhance your travel assistant with **Retrieval Augmented Generation (RAG)** capabilities using the `TextSearchProvider`.

You will implement semantic search over visa policy documents, allowing your agent to answer questions based on up-to-date policy information without retraining the model.

By the end of this lab, you will:

- ✅ Understand how to implement RAG with the Microsoft Agent Framework
- ✅ Use `TextSearchProvider` to search documents before generating responses
- ✅ Implement document chunking and embedding generation
- ✅ Build a travel assistant that answers visa questions with accurate citations

## Instructions

### Step 1: Navigate to the Lab Folder

```bash
cd labs/00-foundations/lab03-rag
```

### Step 2: Run the Program

With .NET 10's file-based apps, you can run the single .cs file directly:

```bash
dotnet run Program.cs
```

Or in Visual Studio Code, open Program.cs and click the **"Run"** button that appears above the code.

### Step 3: Observe the Output

You should see the agent answer visa-related questions by searching through the policy documents and providing accurate responses with citations. Notice how:

- The agent retrieves relevant information from visa policy documents
- Responses include specific policy details and source references
