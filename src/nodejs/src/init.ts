import './long-press.ts'
import SwiftClick from 'swiftclick';
import { Interactive } from 'interactive';

const swiftClick = SwiftClick.attach(document.body);
swiftClick.useCssParser(true); // Enables swiftclick-ignore class support
Interactive.init();
