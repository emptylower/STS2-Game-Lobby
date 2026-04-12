ARG NODE_IMAGE=node:20-bookworm-slim

FROM ${NODE_IMAGE} AS build

WORKDIR /app

COPY package.json package-lock.json tsconfig.json ./
RUN npm ci

COPY src ./src
RUN npm run build && npm prune --omit=dev

FROM ${NODE_IMAGE} AS runtime

WORKDIR /app
ENV NODE_ENV=production

COPY --from=build /app/package.json /app/package-lock.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist

RUN mkdir -p /app/data

VOLUME ["/app/data"]
EXPOSE 8787/tcp

CMD ["node", "--enable-source-maps", "dist/server.js"]
