# syntax=docker/dockerfile:1
# Jekyll builder image for the Repitito documentation site
FROM ruby:3.2-slim

ENV LANG=C.UTF-8 \
    BUNDLE_PATH=/usr/local/bundle \
    BUNDLE_BIN=/usr/local/bundle/bin

# Install build tools and Node.js (required by Jekyll for assets)
RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential git curl nodejs \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /site

# Default command can be overridden by docker-compose
CMD ["bundle", "exec", "jekyll", "build"]
