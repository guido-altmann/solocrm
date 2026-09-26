#!/bin/sh
# Container entrypoint: apply pending EF Core migrations, then start the app.
# The migrations bundle reads the connection string from ConnectionStrings__Crm (same as the app).
set -eu

echo "Applying database migrations..."
./efbundle

echo "Starting SoloCrm.Web..."
exec dotnet SoloCrm.Web.dll "$@"
