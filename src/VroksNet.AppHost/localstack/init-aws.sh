#!/bin/bash
# Runs inside the LocalStack container once it's ready (AppHost.cs "localstack"): the entities of
# docs/samples/order-events-aws-asyncapi.yaml, in LocalStack's default account and region.
set -euo pipefail

awslocal sqs create-queue --queue-name order-fulfilment
awslocal sns create-topic --name order-placed
# A queue subscribed to the topic and dedicated to VroksNet, for Listen's "queue" broker option.
awslocal sqs create-queue --queue-name vroksnet-order-placed
awslocal sns subscribe \
  --topic-arn arn:aws:sns:us-east-1:000000000000:order-placed \
  --protocol sqs \
  --notification-endpoint arn:aws:sqs:us-east-1:000000000000:vroksnet-order-placed \
  --attributes RawMessageDelivery=true
