#!/bin/bash

# Matrix Server Manager - First-Time Setup Script
# This script installs all dependencies needed to run the Matrix Server

set -e  # Exit on error

echo "=================================="
echo "Matrix Server Manager - Setup"
echo "=================================="
echo ""

# Check if Homebrew is installed
if ! command -v brew &> /dev/null; then
    echo "❌ Homebrew is not installed"
    echo "Please install Homebrew from https://brew.sh"
    exit 1
fi

echo "✓ Homebrew found"

# Install Python 3.11 if not already installed
if ! command -v python3.11 &> /dev/null; then
    echo ""
    echo "Installing Python 3.11..."
    brew install python@3.11
    echo "✓ Python 3.11 installed"
else
    echo "✓ Python 3.11 already installed"
fi

# Install PostgreSQL 15 if not already installed
if ! command -v postgres &> /dev/null; then
    echo ""
    echo "Installing PostgreSQL 15..."
    brew install postgresql@15
    echo "✓ PostgreSQL 15 installed"
else
    echo "✓ PostgreSQL 15 already installed"
fi

# Check if .NET SDK is installed
if ! command -v dotnet &> /dev/null; then
    echo ""
    echo "❌ .NET SDK is not installed"
    echo "Please install .NET 8.0 or later from https://dotnet.microsoft.com/download"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo "✓ .NET SDK $DOTNET_VERSION found"

# Create Python virtual environment for Synapse
echo ""
echo "Setting up Python virtual environment..."
VENV_PATH="$HOME/.synapse_venv"

if [ ! -d "$VENV_PATH" ]; then
    echo "Creating virtual environment at $VENV_PATH..."
    /opt/homebrew/bin/python3.11 -m venv "$VENV_PATH"
    echo "✓ Virtual environment created"
else
    echo "✓ Virtual environment already exists"
fi

# Activate virtual environment and install dependencies
echo ""
echo "Installing Python dependencies..."
source "$VENV_PATH/bin/activate"

# Upgrade pip
pip install --upgrade pip

# Install matrix-synapse and required packages
echo "Installing matrix-synapse..."
pip install matrix-synapse

echo "✓ matrix-synapse installed"

# Install optional but required packages
echo "Installing additional dependencies..."
pip install lxml psycopg2-binary
echo "✓ Additional dependencies installed"

# Build the .NET project
echo ""
echo "Building .NET project..."
dotnet build matrix_server_dotnet.sln
echo "✓ .NET project built successfully"

# Create initial config directory
echo ""
echo "Creating configuration directories..."
mkdir -p ~/.synapse
echo "✓ Configuration directories created"

# Generate Synapse configuration
echo ""
echo "Generating Synapse configuration..."
synapse_homeserver --generate-config -H matrix.example.com -c ~/.synapse/homeserver.yaml --report-stats=no
echo "✓ Synapse configuration generated"

echo ""
echo "=================================="
echo "✓ Setup Complete!"
echo "=================================="
echo ""
echo "Next steps:"
echo "1. Edit ~/.synapse/homeserver.yaml to configure your server"
echo "2. Update the domain name from 'matrix.example.com' if needed"
echo "3. Run: dotnet run"
echo "4. Select 'start' to launch the Matrix server"
echo ""
echo "To deactivate the Python environment later, run: deactivate"
