import './long-press-event.js'
import SwiftClick from 'swiftclick';
import { NextInteraction } from 'next-interaction';

const swiftClick = SwiftClick.attach(document.body);
swiftClick.useCssParser(true); // Enables swiftclick-ignore class support
NextInteraction.start();
