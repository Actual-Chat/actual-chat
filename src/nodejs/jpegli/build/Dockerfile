# Reproducible jpegli -> WebAssembly build.
#   docker build -o dist .
# Produces dist/{scalar,simd}/jpegli.{js,wasm}, dist/BUILD_INFO.txt
ARG EMSDK_TAG=6.0.9
FROM emscripten/emsdk:${EMSDK_TAG} AS build
ARG JPEGLI_COMMIT=031a0077f5799a6041004267fc12b956c1f52a20
RUN git clone https://github.com/google/jpegli.git /src/jpegli \
 && cd /src/jpegli && git checkout ${JPEGLI_COMMIT} \
 && git submodule update --init --depth 1 third_party/highway third_party/libjpeg-turbo third_party/skcms
COPY build.sh jpegli_wasm.cc /work/
RUN sed -i 's/\r$//' /work/build.sh && OUT=/out BUILD_ROOT=/src/build bash /work/build.sh

FROM scratch
COPY --from=build /out /
