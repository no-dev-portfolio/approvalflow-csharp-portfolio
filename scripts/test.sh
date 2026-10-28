#!/bin/sh
set -eu
dotnet restore tests/ApprovalFlow.Tests --locked-mode
dotnet test tests/ApprovalFlow.Tests -c Release --no-restore --logger 'trx;LogFileName=integration.trx'
