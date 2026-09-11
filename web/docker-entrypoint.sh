#!/bin/sh
set -eu

# Runs as one of nginx's /docker-entrypoint.d/ pre-start scripts (invoked by the
# base image's own entrypoint before it execs nginx) — regenerate env-config.js
# from the environment at container start, so the same image can be pointed at a
# different API without rebuilding it.
cat <<EOF > /usr/share/nginx/html/env-config.js
window.__ENV__ = {
  API_BASE_URL: "${API_BASE_URL:-}",
}
EOF
