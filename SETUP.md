# Faculty Evaluation System — Local Setup Guide

## Prerequisites

Install the following software in order:

### 1. Git
- **Download:** https://git-scm.com/download/win
- Run the installer with default settings
- Verify: open a terminal and run `git --version`

### 2. Visual Studio Code
- **Download:** https://code.visualstudio.com/download
- Install the **C# Dev Kit** extension from the Extensions panel (Ctrl+Shift+X, search "C# Dev Kit")

### 3. .NET 10 SDK
- **Download:** https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- Download the **SDK** installer (not Runtime) for Windows x64
- Verify: open a terminal and run `dotnet --version` (should show `10.x.x`)

### 4. SQL Server
- **Download (Developer Edition — free):** https://www.microsoft.com/en-us/sql-server/sql-server-downloads
- Choose **Developer** edition and run the installer
- During setup, enable **SQL Server Authentication** (Mixed Mode) and set the `sa` password

### 5. SQL Server Management Studio (SSMS) — Optional
- **Download:** https://learn.microsoft.com/en-us/ssms/download-sql-server-management-studio-ssms
- Useful for viewing/managing the database directly

---

## Setup Steps

### Step 1: Clone the Repository

```bash
git clone https://github.com/engelbertlamercanceran/FacultyEvaluationSystem.git
cd FacultyEvaluationSystem
```

### Step 2: Configure the Database Connection

Open `appsettings.json` and update the connection string to match your SQL Server:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER_NAME;Database=isu_eval;User Id=sa;Password=YOUR_SA_PASSWORD;TrustServerCertificate=true"
  }
}
```

Replace:
- `YOUR_SERVER_NAME` — your SQL Server instance name (e.g., `LAPTOP-ABC123`, `localhost`, or `.\SQLEXPRESS`)
- `YOUR_SA_PASSWORD` — the `sa` password you set during SQL Server installation

**To find your server name:** Open SSMS, the server name is shown in the Connect dialog. Or run `sqlcmd -L` in a terminal.

### Step 3: Install EF Core Tools

```bash
dotnet tool install --global dotnet-ef
```

### Step 4: Create the Database

```bash
dotnet ef database update
```

This creates the `isu_eval` database and all tables automatically from the migrations.

### Step 5: Run the Application

```bash
dotnet run
```

The app will start and show a URL like:

```
Now listening on: http://localhost:5059
```

Open that URL in your browser.

### Step 6: Login

The system is seeded with a default admin account:

| Email              | Password   |
|--------------------|------------|
| admin@isuc.edu.ph  | Admin@123  |

### Step 7: Load Demo Data (Optional)

To populate the system with sample data for demonstration:

1. Log in as admin
2. Navigate to **Batch Import** (in the sidebar)
3. Click **"Run Quick Demo Setup"**

This creates: 5 colleges, 4 programs, 34 users (3 deans, 1 program chair, 10 faculty, 20 students), 3 semesters, 3 evaluation periods, subject assignments, enrollments, sample evaluations, and computed results — all in one click.

---

## Opening in VS Code

1. Open VS Code
2. **File → Open Folder** → select the `FacultyEvaluationSystem` folder
3. If prompted, install recommended extensions
4. Open the integrated terminal (Ctrl+`)
5. Run `dotnet run`

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| `dotnet` not recognized | Restart your terminal after installing .NET SDK |
| Database connection failed | Check your server name and `sa` password in `appsettings.json`. Make sure SQL Server service is running. |
| Port already in use | Stop any other instance of the app, or change the port in `Properties/launchSettings.json` |
| Build fails with file locked error | Stop the running app first (Ctrl+C), then rebuild |
| EF tools not found | Run `dotnet tool install --global dotnet-ef` |

---

## Tech Stack

- **.NET 10** — ASP.NET Core MVC
- **Entity Framework Core** — ORM / database access
- **SQL Server** — relational database
- **ASP.NET Core Identity** — authentication & role management
- **QuestPDF** — PDF report generation
- **Chart.js** — dashboard charts
- **Bootstrap 5** — responsive UI
