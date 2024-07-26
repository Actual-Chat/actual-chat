.DEFAULT_GOAL := run

DOCKER_IMAGE =

.PHONY: build
build:
	$(eval DOCKER_IMAGE=$(shell docker build -q .))

.PHONY: run
run: build
	echo "Image: $(DOCKER_IMAGE)"
	docker run --rm -it -p 8081:8081 --env-file .secrets ${DOCKER_IMAGE}
